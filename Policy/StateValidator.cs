using System;
using System.Text;

namespace KKAiChatGateway.Policy
{
    public class StateValidator
    {
        private static readonly string[] EncodedIdentifiers = new[]
        {
            "VGVyYXVjaGkgRW1p",
            "VGVyYXVjaGkgQ2hpZW1p",
            "5a+65YaF5oG1576O"
        };
        private const int RequiredPersonality = 25;

        public ValidationResult Evaluate(string characterName, int personalityId, bool authorized)
        {
            if (!IsMatch(characterName, personalityId))
                return new ValidationResult { RequiresAuthorization = false };

            if (authorized)
                return new ValidationResult { RequiresAuthorization = false, IsProtectedProfile = true };

            return new ValidationResult
            {
                IsProtectedProfile = true,
                RequiresAuthorization = true,
                PromptB64 = StringResources.RestrictedInteractionPromptB64,
                AffectionBias = -10,
                TrustBias = -10,
                ArousalBias = -20,
                AngerBias = 10
            };
        }

        private bool IsMatch(string characterName, int personalityId)
        {
            if (personalityId != RequiredPersonality) return false;
            if (string.IsNullOrEmpty(characterName)) return false;

            foreach (var encoded in EncodedIdentifiers)
            {
                var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(encoded));
                if (string.Equals(characterName, decoded, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }

    public class ValidationResult
    {
        public bool IsProtectedProfile { get; set; }
        public bool RequiresAuthorization { get; set; }
        public string PromptB64 { get; set; }
        public int AffectionBias { get; set; }
        public int TrustBias { get; set; }
        public int ArousalBias { get; set; }
        public int AngerBias { get; set; }
    }
}
