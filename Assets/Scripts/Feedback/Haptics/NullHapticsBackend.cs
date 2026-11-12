/// <summary>
/// Safe no-op haptics backend used when running in the Unity Editor or on unsupported platforms.
/// </summary>
public sealed class NullHapticsBackend : IHapticsBackend
{
    public static readonly NullHapticsBackend Instance = new NullHapticsBackend();

    public bool IsSupported => false;

    public void Play(HapticEvent hapticEvent)
    {
    }
}

