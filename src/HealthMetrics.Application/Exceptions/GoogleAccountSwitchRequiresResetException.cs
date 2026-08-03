namespace HealthMetrics.Application.Exceptions;

public sealed class GoogleAccountSwitchRequiresResetException(string existingAccount, string newAccount)
    : InvalidOperationException(
        $"This dashboard already contains local health history for {existingAccount}. Clear the local database before connecting {newAccount}.")
{
    public string ExistingAccount { get; } = existingAccount;

    public string NewAccount { get; } = newAccount;
}
