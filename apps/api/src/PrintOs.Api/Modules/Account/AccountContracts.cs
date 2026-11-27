namespace PrintOs.Api.Modules.Account;

public sealed record ExportAccountRequest(string Password);

public sealed record DeleteAccountRequest(string Password, string WorkspaceName);
