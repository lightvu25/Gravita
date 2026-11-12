using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor utility to generate the six default Power-up ScriptableObject assets for Gravita.
/// Runnable via menu: Tools -> Gravita -> Power-ups -> Generate Power-up Data
/// </summary>
public static class PowerUpDataEditorTool
{
    public const string DestinationFolder = "Assets/Data/PowerUps";

    private struct PowerUpSpec
    {
        public string FileName;
        public string Id;
        public string DisplayName;
        public string Description;
        public PowerUpType Type;
        public float Duration;
        public float EffectValue;
        public int UsageLimit;
        public float Cooldown;
        public bool IsConsumable;
        public float PullSpeed;
        public int MaxWallCells;
        public int MaxTrapsDestroyed;
        public int MinWallTraversal;
        public int MaxWallTraversal;
    }

    private static readonly PowerUpSpec[] PowerUpSpecs =
    {
        new PowerUpSpec
        {
            FileName = "Shield.asset",
            Id = "shield",
            DisplayName = "Shield",
            Description = "Blocks one instance of damage from a trap or enemy.",
            Type = PowerUpType.Shield,
            Duration = 0f,
            EffectValue = 1f,
            UsageLimit = 1,
            Cooldown = 0f,
            IsConsumable = true,
            PullSpeed = 12f,
            MaxWallCells = 2,
            MaxTrapsDestroyed = 1,
            MinWallTraversal = 1,
            MaxWallTraversal = 3
        },
        new PowerUpSpec
        {
            FileName = "Magnet.asset",
            Id = "magnet",
            DisplayName = "Magnet",
            Description = "Attracts coins within a radius toward the player for 6 seconds.",
            Type = PowerUpType.Magnet,
            Duration = 6f,
            EffectValue = 3f,
            UsageLimit = 1,
            Cooldown = 0f,
            IsConsumable = true,
            PullSpeed = 12f,
            MaxWallCells = 2,
            MaxTrapsDestroyed = 1,
            MinWallTraversal = 1,
            MaxWallTraversal = 3
        },
        new PowerUpSpec
        {
            FileName = "Breaker.asset",
            Id = "breaker",
            DisplayName = "Breaker",
            Description = "Breaks up to two breakable walls and up to one trap in the two cells ahead.",
            Type = PowerUpType.Breaker,
            Duration = 0f,
            EffectValue = 2f,
            UsageLimit = 1,
            Cooldown = 0f,
            IsConsumable = true,
            PullSpeed = 12f,
            MaxWallCells = 2,
            MaxTrapsDestroyed = 1,
            MinWallTraversal = 1,
            MaxWallTraversal = 3
        },
        new PowerUpSpec
        {
            FileName = "Echo.asset",
            Id = "echo",
            DisplayName = "Echo",
            Description = "Creates a stationary Echo at the player's position for 5 seconds; press again or hit a hazard to return.",
            Type = PowerUpType.Echo,
            Duration = 5f,
            EffectValue = 0f,
            UsageLimit = 1,
            Cooldown = 0f,
            IsConsumable = true,
            PullSpeed = 12f,
            MaxWallCells = 2,
            MaxTrapsDestroyed = 1,
            MinWallTraversal = 1,
            MaxWallTraversal = 3
        },
        new PowerUpSpec
        {
            FileName = "Teleport.asset",
            Id = "teleport",
            DisplayName = "Teleport",
            Description = "Teleports the player through 1 to 3 consecutive wall cells in the movement direction to a valid cell on the other side.",
            Type = PowerUpType.Teleport,
            Duration = 0f,
            EffectValue = 3f,
            UsageLimit = 1,
            Cooldown = 0f,
            IsConsumable = true,
            PullSpeed = 12f,
            MaxWallCells = 2,
            MaxTrapsDestroyed = 1,
            MinWallTraversal = 1,
            MaxWallTraversal = 3
        },
        new PowerUpSpec
        {
            FileName = "Freeze.asset",
            Id = "freeze",
            DisplayName = "Freeze",
            Description = "Freezes enemies and traps that support freezing for 4 seconds.",
            Type = PowerUpType.Freeze,
            Duration = 4f,
            EffectValue = 0f,
            UsageLimit = 1,
            Cooldown = 0f,
            IsConsumable = true,
            PullSpeed = 12f,
            MaxWallCells = 2,
            MaxTrapsDestroyed = 1,
            MinWallTraversal = 1,
            MaxWallTraversal = 3
        }
    };

    [MenuItem("Tools/Gravita/Power-ups/Generate Power-up Data")]
    public static void GeneratePowerUpData()
    {
        EnsureFolderExists(DestinationFolder);

        int createdCount = 0;
        int skippedCount = 0;
        var summary = new StringBuilder();
        summary.AppendLine("[PowerUpDataEditorTool] Generating Power-up ScriptableObject assets...");

        foreach (var spec in PowerUpSpecs)
        {
            string assetPath = $"{DestinationFolder}/{spec.FileName}";
            PowerUpData existingAsset = AssetDatabase.LoadAssetAtPath<PowerUpData>(assetPath);

            if (existingAsset != null)
            {
                skippedCount++;
                summary.AppendLine($"  - [SKIPPED] {spec.FileName} already exists at {assetPath}. Preserving existing asset.");
                continue;
            }

            var newAsset = ScriptableObject.CreateInstance<PowerUpData>();
            newAsset.Initialize(
                id: spec.Id,
                displayName: spec.DisplayName,
                description: spec.Description,
                powerUpType: spec.Type,
                duration: spec.Duration,
                effectValue: spec.EffectValue,
                usageLimit: spec.UsageLimit,
                cooldown: spec.Cooldown,
                isConsumable: spec.IsConsumable,
                icon: null,
                pullSpeed: spec.PullSpeed,
                maxWallCells: spec.MaxWallCells,
                maxTrapsDestroyed: spec.MaxTrapsDestroyed,
                minWallTraversal: spec.MinWallTraversal,
                maxWallTraversal: spec.MaxWallTraversal
            );

            AssetDatabase.CreateAsset(newAsset, assetPath);
            createdCount++;
            summary.AppendLine($"  - [CREATED] {spec.FileName} at {assetPath} (ID: {spec.Id}, Type: {spec.Type})");
        }

        if (createdCount > 0)
        {
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        summary.AppendLine($"[PowerUpDataEditorTool] Finished. Created: {createdCount}, Skipped: {skippedCount}.");
        Debug.Log(summary.ToString());
    }

    private static void EnsureFolderExists(string folderPath)
    {
        if (AssetDatabase.IsValidFolder(folderPath))
        {
            return;
        }

        string[] parts = folderPath.Split('/');
        string current = parts[0];

        for (int i = 1; i < parts.Length; i++)
        {
            string next = $"{current}/{parts[i]}";
            if (!AssetDatabase.IsValidFolder(next))
            {
                AssetDatabase.CreateFolder(current, parts[i]);
            }
            current = next;
        }
    }
}
