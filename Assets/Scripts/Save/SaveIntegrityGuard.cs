using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

/// <summary>
/// Computes and verifies HMAC-SHA256 sidecar signatures (<c>.sig</c>) alongside serialized save files (L5).
/// </summary>
public static class SaveIntegrityGuard
{
    private const string SignatureExtension = ".sig";
    private const string AppSalt = "Gravita_Save_HMAC_v1_#9fA2!kLp";

    public static string GetSignaturePath(string jsonFilePath) =>
        string.IsNullOrEmpty(jsonFilePath) ? string.Empty : jsonFilePath + SignatureExtension;

    public static string ComputeSignature(string jsonContent)
    {
        if (jsonContent == null)
            return string.Empty;

        byte[] keyBytes = ResolveKeyBytes();
        byte[] payloadBytes = Encoding.UTF8.GetBytes(jsonContent);
        using var hmac = new HMACSHA256(keyBytes);
        byte[] hash = hmac.ComputeHash(payloadBytes);
        var sb = new StringBuilder(hash.Length * 2);
        for (int i = 0; i < hash.Length; i++)
            sb.Append(hash[i].ToString("x2"));
        return sb.ToString();
    }

    public static void WriteSignature(string jsonFilePath, string jsonContent)
    {
        string sigPath = GetSignaturePath(jsonFilePath);
        if (string.IsNullOrEmpty(sigPath))
            return;

        File.WriteAllText(sigPath, ComputeSignature(jsonContent));
    }

    public static bool VerifyFileSignature(string jsonFilePath, string jsonContent)
    {
        string sigPath = GetSignaturePath(jsonFilePath);
        if (string.IsNullOrEmpty(sigPath) || !File.Exists(sigPath))
            return false;

        string stored = (File.ReadAllText(sigPath) ?? string.Empty).Trim();
        string expected = ComputeSignature(jsonContent);
        return FixedTimeEquals(stored, expected);
    }

    public static void CopySignatureIfExists(string sourceJsonPath, string destJsonPath)
    {
        string srcSig = GetSignaturePath(sourceJsonPath);
        string dstSig = GetSignaturePath(destJsonPath);
        if (string.IsNullOrEmpty(srcSig) || string.IsNullOrEmpty(dstSig) || !File.Exists(srcSig))
            return;

        File.Copy(srcSig, dstSig, overwrite: true);
    }

    public static void MoveSignatureIfExists(string sourceJsonPath, string destJsonPath)
    {
        string srcSig = GetSignaturePath(sourceJsonPath);
        string dstSig = GetSignaturePath(destJsonPath);
        if (string.IsNullOrEmpty(srcSig) || string.IsNullOrEmpty(dstSig) || !File.Exists(srcSig))
            return;

        if (File.Exists(dstSig))
            File.Delete(dstSig);

        File.Move(srcSig, dstSig);
    }

    private static byte[] ResolveKeyBytes()
    {
        string deviceId;
        try
        {
            deviceId = SystemInfo.deviceUniqueIdentifier;
        }
        catch
        {
            deviceId = "fallback_device";
        }

        if (string.IsNullOrWhiteSpace(deviceId) || deviceId == SystemInfo.unsupportedIdentifier)
            deviceId = "gravita_default_device";

        return Encoding.UTF8.GetBytes($"{AppSalt}|{deviceId}");
    }

    private static bool FixedTimeEquals(string a, string b)
    {
        if (a == null || b == null || a.Length != b.Length)
            return false;

        int diff = 0;
        for (int i = 0; i < a.Length; i++)
            diff |= a[i] ^ b[i];
        return diff == 0;
    }
}
