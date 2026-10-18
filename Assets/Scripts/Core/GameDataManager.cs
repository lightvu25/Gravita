using System.Collections.Generic;
using UnityEngine;

public class GameDataManager : MonoBehaviour
{
    public static GameDataManager Instance { get; private set; }

    private static ProfileData Profile => GameSession.Instance?.currentProfile;

    private readonly HashSet<string> persistenceKeys = new HashSet<string>();

    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(gameObject); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        if (Profile != null && Profile.persistenceKeys != null)
        {
            foreach (var key in Profile.persistenceKeys)
                persistenceKeys.Add(key);
        }
    }

    public void AddPersistenceKey(string key)
    {
        if (string.IsNullOrEmpty(key) || persistenceKeys.Contains(key)) return;

        persistenceKeys.Add(key);
        
        if (Profile != null)
        {
            if (!Profile.persistenceKeys.Contains(key))
                Profile.persistenceKeys.Add(key);
                
            SaveManager.saveProfile(Profile);
        }
    }

    public bool HasPersistenceKey(string key)
    {
        if (string.IsNullOrEmpty(key)) return false;
        return persistenceKeys.Contains(key);
    }

    // ------------------------------------------------------------------ //
    //  Level Attempt Tracking                                              //
    // ------------------------------------------------------------------ //

    public void IncrementLevelAttempt(int levelIndex)
    {
        if (Profile == null) return;
        Profile.IncrementLevelAttempt(levelIndex);
        SaveManager.saveProfile(Profile);
    }

    public int GetLevelAttemptCount(int levelIndex) =>
        Profile != null ? Profile.GetLevelAttempts(levelIndex) : 0;
}