namespace ARIS1.Services
{
    public static class PasswordGenerator
    {
        private const string Lower = "abcdefghijkmnopqrstuvwxyz";
        private const string Upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
        private const string Digits = "23456789";
        private const string Special = "!@#$%^&*";
        private const string All = Lower + Upper + Digits + Special;

        public static string Generate()
        {
            var rng = Random.Shared;

            var chars = new List<char>
            {
                Lower[rng.Next(Lower.Length)],
                Upper[rng.Next(Upper.Length)],
                Digits[rng.Next(Digits.Length)],
                Special[rng.Next(Special.Length)]
            };

            for (int i = 0; i < 8; i++)
                chars.Add(All[rng.Next(All.Length)]);

            return new string(chars.OrderBy(_ => rng.Next()).ToArray());
        }
    }
}
