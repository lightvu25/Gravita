// By SOLO :)
// Check MOST IN ONE package https://assetstore.unity.com/packages/slug/295013

using UnityEngine;
using UnityEngine.UI;

namespace Solo.MOST_IN_ONE
{
    // This is an optional example for all haptic feedback calls.
    // from any .cs file inside your project
    // call >>> MOST_HapticFeedback.Generate(HapticTypes type) for any hapitc type
    // or MOST_HapticFeedback.GenerateWithCooldown(HapticTypes type, float cooldown)

    public class HapticsExample : MonoBehaviour
    {
        public Toggle HapticToggle;
        public MOST_HapticFeedback.CustomHapticPattern CustomHapticPattern;
        public MOST_HapticFeedback.HapticCurve Curve;

        void Start()
        {
            HapticToggle.isOn = MOST_HapticFeedback.HapticsEnabled;
        }

        public void GenerateBasicHaptic(MOST_HapticFeedback.HapticTypes type)
        {
            MOST_HapticFeedback.Generate(type);
        }

        public void GenerateBasicHapticWithCoolDown(MOST_HapticFeedback.HapticTypes type, float cooldown)
        {
            MOST_HapticFeedback.GenerateWithCooldown(type, cooldown);
        }

        public void GenerateCustomHapticPattern()
        {
            //MOST_HapticFeedback.Generate(CustomHapticPattern);
            MOST_HapticFeedback.GeneratePattern(CustomHapticPattern);
        }

        public void GenerateCustomHapticCurve()
        {
            //MOST_HapticFeedback.Generate(Curve);
            MOST_HapticFeedback.GenerateCurve(Curve);
        }

        public void HapticEnable(bool enable)
        {
            MOST_HapticFeedback.HapticsEnabled = enable;
        }

        // __________________________________ Basic Haptics __________________________________
        public void SelectionHaptic()
        {
            MOST_HapticFeedback.Generate(MOST_HapticFeedback.HapticTypes.Selection);
        }

        public void SuccessHaptic()
        {
            MOST_HapticFeedback.Generate(MOST_HapticFeedback.HapticTypes.Success);
        }

        public void WarningHaptic()
        {
            MOST_HapticFeedback.Generate(MOST_HapticFeedback.HapticTypes.Warning);
        }

        public void FailureHaptic()
        {
            MOST_HapticFeedback.Generate(MOST_HapticFeedback.HapticTypes.Failure);
        }

        public void LightImpactHaptic()
        {
            MOST_HapticFeedback.Generate(MOST_HapticFeedback.HapticTypes.LightImpact);
        }

        public void MediumImpactHaptic()
        {
            MOST_HapticFeedback.Generate(MOST_HapticFeedback.HapticTypes.MediumImpact);
        }

        public void HeavyImpactHaptic()
        {
            MOST_HapticFeedback.Generate(MOST_HapticFeedback.HapticTypes.HeavyImpact);
        }

        public void RigidImpactHaptic()
        {
            MOST_HapticFeedback.Generate(MOST_HapticFeedback.HapticTypes.RigidImpact);
        }

        public void SoftImpactHaptic()
        {
            MOST_HapticFeedback.Generate(MOST_HapticFeedback.HapticTypes.SoftImpact);
        }

        // __________________________________ Basic Haptics with Cooldown __________________________________ 
        public void SelectionHapticWithCooldown(float cooldown)
        {
            MOST_HapticFeedback.GenerateWithCooldown(MOST_HapticFeedback.HapticTypes.Selection, cooldown);
        }

        public void SuccessHapticWithCooldown(float cooldown)
        {
            MOST_HapticFeedback.GenerateWithCooldown(MOST_HapticFeedback.HapticTypes.Success, cooldown);
        }

        public void WarningHapticWithCooldown(float cooldown)
        {
            MOST_HapticFeedback.GenerateWithCooldown(MOST_HapticFeedback.HapticTypes.Warning, cooldown);
        }

        public void FailureHapticWithCooldown(float cooldown)
        {
            MOST_HapticFeedback.GenerateWithCooldown(MOST_HapticFeedback.HapticTypes.Failure, cooldown);
        }

        public void LightImpactHapticWithCooldown(float cooldown)
        {
            MOST_HapticFeedback.GenerateWithCooldown(MOST_HapticFeedback.HapticTypes.LightImpact, cooldown);
        }

        public void MediumImpactHapticWithCooldown(float cooldown)
        {
            MOST_HapticFeedback.GenerateWithCooldown(MOST_HapticFeedback.HapticTypes.MediumImpact, cooldown);
        }

        public void HeavyImpactHapticWithCooldown(float cooldown)
        {
            MOST_HapticFeedback.GenerateWithCooldown(MOST_HapticFeedback.HapticTypes.HeavyImpact, cooldown);
        }

        public void RigidImpactHapticWithCooldown(float cooldown)
        {
            MOST_HapticFeedback.GenerateWithCooldown(MOST_HapticFeedback.HapticTypes.RigidImpact, cooldown);
        }

        public void SoftImpactHapticWithCooldown(float cooldown)
        {
            MOST_HapticFeedback.GenerateWithCooldown(MOST_HapticFeedback.HapticTypes.SoftImpact, cooldown);
        }

        // ___________________ Enable / Disable Haptic Feedback ___________________  
        public void ToggleHaptics(bool enabled)
        {
            MOST_HapticFeedback.HapticsEnabled = enabled;
        }

        // Opem URL
        public void OpenURL()
        {
            Application.OpenURL("https://assetstore.unity.com/packages/slug/295013");
        }
    }
}