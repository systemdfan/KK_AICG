using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using BepInEx.Logging;

namespace KKAiChatGateway.Policy
{   
    public class KeyResolver
    {
        private const string DefaultPublicKeyXml = "<RSAKeyValue><Modulus>8WfJBj9cH95uCfwwSDmtFFlARNrqLze7gOUeOr4nFEit74pX+lR49OPjI1YoX7MWwfNzrqGfBI1ictJv97QuqMkBk2TDbuCeG0HUIdsZfye8dZNp1PbAr2qREBQ/QPSXS/nXwXaHHwtWqs8ABaqn5+/wgmT2kgs9494RFz/41tAqDTc9cUQvbjZadkRSM16ySzwGUpmsNu/BKJsBMDeMhAeIBRG3qRi2qOCVEO9GKRJ14JORZ3Y6aZJU1gL0mppsesVUBbi2lKubdgiYFQUWBy9nlkFdLuixFf4KeRplkBetjuXxebarI1fn1aAoJXvpamQaFCz4SIe+qMZCdh/axQ==</Modulus><Exponent>AQAB</Exponent></RSAKeyValue>";

        private readonly string _configDir;
        private readonly ManualLogSource _log;

        public KeyResolver(string configDirectory, ManualLogSource log)
        {
            _configDir = configDirectory;
            _log = log;
        }

        public bool IsAuthorized()
        {
            string keyPath = Path.Combine(_configDir, "kkaicg.author");
            _log.LogInfo("[kkaicg] Looking for author key at: " + keyPath);

            if (!File.Exists(keyPath))
            {
                _log.LogInfo("[kkaicg] Author key file not found.");
                return false;
            }

            try
            {
                var raw = File.ReadAllText(keyPath).Trim();
                _log.LogInfo("[kkaicg] Author key file loaded, length: " + raw.Length);

                var separator = raw.LastIndexOf('|');
                if (separator <= 0)
                {
                    _log.LogWarning("[kkaicg] Author key file has invalid format (no '|' separator).");
                    return false;
                }

                var message = raw.Substring(0, separator);
                var signatureB64 = raw.Substring(separator + 1);
                _log.LogInfo("[kkaicg] Author key message: " + message);

                var signature = Convert.FromBase64String(signatureB64);
                var publicKeyXml = LoadPublicKey();
                var result = Verify(publicKeyXml, message, signature);
                _log.LogInfo("[kkaicg] Author key verification result: " + result);
                return result;
            }
            catch (Exception ex)
            {
                _log.LogWarning("[kkaicg] Author key verification failed: " + ex.GetType().Name + ": " + ex.Message);
                return false;
            }
        }

        private string LoadPublicKey()
        {
            string overridePath = Path.Combine(_configDir, "kkaicg.public");
            if (File.Exists(overridePath))
            {
                _log.LogInfo("[kkaicg] Loading override public key from: " + overridePath);
                return File.ReadAllText(overridePath).Trim();
            }
            _log.LogInfo("[kkaicg] Using default embedded public key.");
            return DefaultPublicKeyXml;
        }

        private bool Verify(string publicKeyXml, string message, byte[] signature)
        {
            using (var rsa = new RSACryptoServiceProvider())
            {
                rsa.FromXmlString(publicKeyXml);
                var data = Encoding.UTF8.GetBytes(message);
                try
                {
                    if (rsa.VerifyData(data, "SHA256", signature))
                    {
                        _log.LogInfo("[kkaicg] Verification via RSACryptoServiceProvider.VerifyData(SHA256).");
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    
                    _log.LogInfo("[kkaicg] VerifyData(SHA256) unavailable (" + ex.GetType().Name + "), using deformatter fallback.");
                }
                try
                {
                    byte[] hash;
                    using (var sha = new SHA256Managed())
                        hash = sha.ComputeHash(data);

                    var deformatter = new RSAPKCS1SignatureDeformatter(rsa);
                    deformatter.SetHashAlgorithm("SHA256");
                    var result = deformatter.VerifySignature(hash, signature);
                    _log.LogInfo("[kkaicg] Verification via RSAPKCS1SignatureDeformer(SHA256Managed): " + result);
                    return result;
                }
                catch (Exception ex)
                {
                    _log.LogWarning("[kkaicg] SHA256 verification unsupported by this runtime: " + ex.GetType().Name + ": " + ex.Message);
                    return false;
                }
            }
        }
    }
}
