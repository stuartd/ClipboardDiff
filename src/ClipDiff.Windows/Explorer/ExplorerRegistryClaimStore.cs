using Microsoft.Win32;

namespace ClipDiff.Windows.Explorer;

internal sealed class ExplorerRegistryClaimStore : IExplorerRegistrationClaimStore
{
	private const string KeyPath = @"Software\ClipDiff\ExplorerOwners";

	public IReadOnlyList<ExplorerRegistrationClaim> ReadClaims()
	{
		using var ownersKey = Registry.CurrentUser.OpenSubKey(KeyPath);
		var claims = new List<ExplorerRegistrationClaim>();

		foreach (var ownerId in ownersKey?.GetSubKeyNames() ?? [])
		{
			using var ownerKey = ownersKey!.OpenSubKey(ownerId);

			if (ownerKey is null)
			{
				continue;
			}

			claims.Add(new ExplorerRegistrationClaim(
				ownerId,
				Equals(ownerKey.GetValue("SingleEnabled"), 1),
				ReadString(ownerKey, "DisplayName"),
				ReadString(ownerKey, "SingleCommandLine"),
				ReadString(ownerKey, "IconPath"),
				Equals(ownerKey.GetValue("PairEnabled"), 1),
				ReadString(ownerKey, "ComServerCommandLine"),
				ReadString(ownerKey, "ExtensionPath")));
		}

		return claims;
	}

	public void WriteClaim(ExplorerRegistrationClaim claim)
	{
		using var ownerKey = Registry.CurrentUser.CreateSubKey(KeyPath + @"\" + claim.OwnerId);
		ownerKey.SetValue("SingleEnabled", 0, RegistryValueKind.DWord);
		ownerKey.SetValue("PairEnabled", 0, RegistryValueKind.DWord);
		ownerKey.SetValue("DisplayName", claim.DisplayName, RegistryValueKind.String);
		ownerKey.SetValue("SingleCommandLine", claim.SingleCommandLine, RegistryValueKind.String);
		ownerKey.SetValue("IconPath", claim.IconPath, RegistryValueKind.String);
		ownerKey.SetValue("ComServerCommandLine", claim.ComServerCommandLine, RegistryValueKind.String);
		ownerKey.SetValue("ExtensionPath", claim.ExtensionPath, RegistryValueKind.String);
		ownerKey.SetValue("SingleEnabled", claim.SingleEnabled ? 1 : 0, RegistryValueKind.DWord);
		ownerKey.SetValue("PairEnabled", claim.PairEnabled ? 1 : 0, RegistryValueKind.DWord);
	}

	public void DeleteClaim(string ownerId) =>
		Registry.CurrentUser.DeleteSubKeyTree(KeyPath + @"\" + ownerId, throwOnMissingSubKey: false);

	private static string ReadString(RegistryKey key, string name) => key.GetValue(name) as string ?? string.Empty;
}
