namespace HonestFlow.Application.Auth
{
    public sealed class SellerCredentials
    {
        public SellerCredentials(string login, string password)
        {
            Login = login;
            Password = password;
        }

        public string Login { get; }
        public string Password { get; }
    }
}
