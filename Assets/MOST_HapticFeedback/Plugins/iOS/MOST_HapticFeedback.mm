// By SOLO :)
// Unity iOS native plugin for preset haptics + Core Haptics pulse curves.

#import <Foundation/Foundation.h>
#import <UIKit/UIKit.h>
#import <math.h>

#if __has_feature(modules)
@import CoreHaptics;
#else
#import <CoreHaptics/CoreHaptics.h>
#endif

static UISelectionFeedbackGenerator *gSelection = nil;
static UINotificationFeedbackGenerator *gNotif = nil;
static UIImpactFeedbackGenerator *gImpactLight = nil;
static UIImpactFeedbackGenerator *gImpactMedium = nil;
static UIImpactFeedbackGenerator *gImpactHeavy = nil;
static UIImpactFeedbackGenerator *gImpactRigid = nil; // iOS 13+
static UIImpactFeedbackGenerator *gImpactSoft = nil;  // iOS 13+

static CHHapticEngine *gCoreEngine = nil;
static id<CHHapticPatternPlayer> gCorePlayer = nil;

static CFTimeInterval gLastHit = 0;
static const CFTimeInterval kMinInterval = 0.02; // 20 ms, only for preset taps.

static float MOST_ClampFloat(float value, float minValue, float maxValue)
{
    if (!isfinite(value)) return minValue;
    if (value < minValue) return minValue;
    if (value > maxValue) return maxValue;
    return value;
}

static void EnsureInit(void)
{
    if (@available(iOS 10.0, *))
    {
        if (!gSelection)
        {
            gSelection = [UISelectionFeedbackGenerator new];
            gNotif = [UINotificationFeedbackGenerator new];
            gImpactLight = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleLight];
            gImpactMedium = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleMedium];
            gImpactHeavy = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleHeavy];

            if (@available(iOS 13.0, *))
            {
                gImpactRigid = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleRigid];
                gImpactSoft = [[UIImpactFeedbackGenerator alloc] initWithStyle:UIImpactFeedbackStyleSoft];
            }
        }
    }
}

static BOOL RateLimited(void)
{
    CFTimeInterval now = CFAbsoluteTimeGetCurrent();

    if (now - gLastHit < kMinInterval)
        return YES;

    gLastHit = now;
    return NO;
}

static BOOL MOST_EnsureCoreHaptics(void)
{
    if (@available(iOS 13.0, *))
    {
        id<CHHapticDeviceCapability> caps = [CHHapticEngine capabilitiesForHardware];

        if (![caps supportsHaptics])
            return NO;

        NSError *error = nil;

        if (!gCoreEngine)
        {
            gCoreEngine = [[CHHapticEngine alloc] initAndReturnError:&error];

            if (error || !gCoreEngine)
            {
                NSLog(@"MOST CoreHaptics init failed: %@", error);
                return NO;
            }

            gCoreEngine.resetHandler = ^{
                NSError *restartError = nil;
                [gCoreEngine startAndReturnError:&restartError];

                if (restartError)
                    NSLog(@"MOST CoreHaptics restart failed: %@", restartError);
            };

            gCoreEngine.stoppedHandler = ^(CHHapticEngineStoppedReason reason) {
                NSLog(@"MOST CoreHaptics stopped: %ld", (long)reason);
            };
        }

        BOOL started = [gCoreEngine startAndReturnError:&error];

        if (!started || error)
        {
            NSLog(@"MOST CoreHaptics start failed: %@", error);
            return NO;
        }

        return YES;
    }

    return NO;
}

extern "C"
{
    void MOST_HapticPrewarm(void)
    {
        if (@available(iOS 10.0, *))
        {
            dispatch_async(dispatch_get_main_queue(), ^{
                EnsureInit();

                [gSelection prepare];
                [gNotif prepare];
                [gImpactLight prepare];
                [gImpactMedium prepare];
                [gImpactHeavy prepare];

                if (@available(iOS 13.0, *))
                {
                    [gImpactRigid prepare];
                    [gImpactSoft prepare];
                    MOST_EnsureCoreHaptics();
                }
            });
        }
    }

    void MOST_HapticFeedback(int type)
    {
        if (@available(iOS 10.0, *))
        {
            dispatch_async(dispatch_get_main_queue(), ^{
                EnsureInit();

                if (RateLimited())
                    return;

                switch (type)
                {
                    case 0: // Selection
                        [gSelection prepare];
                        [gSelection selectionChanged];
                        break;

                    case 1: // Success
                        [gNotif prepare];
                        [gNotif notificationOccurred:UINotificationFeedbackTypeSuccess];
                        break;

                    case 2: // Warning
                        [gNotif prepare];
                        [gNotif notificationOccurred:UINotificationFeedbackTypeWarning];
                        break;

                    case 3: // Failure
                        [gNotif prepare];
                        [gNotif notificationOccurred:UINotificationFeedbackTypeError];
                        break;

                    case 4: // LightImpact
                        [gImpactLight prepare];
                        [gImpactLight impactOccurred];
                        break;

                    case 5: // MediumImpact
                        [gImpactMedium prepare];
                        [gImpactMedium impactOccurred];
                        break;

                    case 6: // HeavyImpact
                        [gImpactHeavy prepare];
                        [gImpactHeavy impactOccurred];
                        break;

                    case 7: // RigidImpact, iOS 13+
                        if (@available(iOS 13.0, *))
                        {
                            [gImpactRigid prepare];
                            [gImpactRigid impactOccurred];
                        }
                        else
                        {
                            [gImpactHeavy prepare];
                            [gImpactHeavy impactOccurred];
                        }
                        break;

                    case 8: // SoftImpact, iOS 13+
                        if (@available(iOS 13.0, *))
                        {
                            [gImpactSoft prepare];
                            [gImpactSoft impactOccurred];
                        }
                        else
                        {
                            [gImpactLight prepare];
                            [gImpactLight impactOccurred];
                        }
                        break;

                    default:
                        [gImpactMedium prepare];
                        [gImpactMedium impactOccurred];
                        break;
                }
            });
        }
    }

    bool MOST_HapticSupportsCore(void)
    {
        if (@available(iOS 13.0, *))
        {
            id<CHHapticDeviceCapability> caps = [CHHapticEngine capabilitiesForHardware];
            return [caps supportsHaptics] ? true : false;
        }

        return false;
    }

    void MOST_HapticStopCurve(void)
    {
        if (@available(iOS 13.0, *))
        {
            dispatch_async(dispatch_get_main_queue(), ^{
                if (!gCorePlayer)
                    return;

                NSError *error = nil;
                [gCorePlayer stopAtTime:CHHapticTimeImmediate error:&error];

                if (error)
                    NSLog(@"MOST CoreHaptics stop failed: %@", error);

                gCorePlayer = nil;
            });
        }
    }

    void MOST_HapticPulseCurve(
        float durationSeconds,
        float sharpness,
        const float *timesSeconds,
        const float *intensities,
        int count)
    {
        if (count < 2 || timesSeconds == NULL || intensities == NULL)
            return;

        durationSeconds = MOST_ClampFloat(durationSeconds, 0.01f, 30.0f);
        sharpness = MOST_ClampFloat(sharpness, 0.0f, 1.0f);

        NSMutableArray<NSNumber *> *copiedTimes = [NSMutableArray arrayWithCapacity:count];
        NSMutableArray<NSNumber *> *copiedIntensities = [NSMutableArray arrayWithCapacity:count];

        for (int i = 0; i < count; i++)
        {
            float t = MOST_ClampFloat(timesSeconds[i], 0.0f, durationSeconds);
            float v = MOST_ClampFloat(intensities[i], 0.0f, 1.0f);

            [copiedTimes addObject:@(t)];
            [copiedIntensities addObject:@(v)];
        }

        dispatch_async(dispatch_get_main_queue(), ^{
            if (@available(iOS 13.0, *))
            {
                if (!MOST_EnsureCoreHaptics())
                    return;

                if (gCorePlayer)
                {
                    NSError *stopError = nil;
                    [gCorePlayer stopAtTime:CHHapticTimeImmediate error:&stopError];
                    gCorePlayer = nil;
                }

                NSMutableArray<CHHapticParameterCurveControlPoint *> *points =
                    [NSMutableArray arrayWithCapacity:count];

                for (int i = 0; i < count; i++)
                {
                    float t = [[copiedTimes objectAtIndex:i] floatValue];
                    float v = [[copiedIntensities objectAtIndex:i] floatValue];

                    CHHapticParameterCurveControlPoint *point =
                        [[CHHapticParameterCurveControlPoint alloc]
                            initWithRelativeTime:t
                            value:v];

                    [points addObject:point];
                }

                CHHapticEventParameter *baseIntensity =
                    [[CHHapticEventParameter alloc]
                        initWithParameterID:CHHapticEventParameterIDHapticIntensity
                        value:1.0f];

                CHHapticEventParameter *sharpnessParam =
                    [[CHHapticEventParameter alloc]
                        initWithParameterID:CHHapticEventParameterIDHapticSharpness
                        value:sharpness];

                CHHapticEvent *event =
                    [[CHHapticEvent alloc]
                        initWithEventType:CHHapticEventTypeHapticContinuous
                        parameters:@[baseIntensity, sharpnessParam]
                        relativeTime:0.0
                        duration:durationSeconds];

                CHHapticParameterCurve *intensityCurve =
                    [[CHHapticParameterCurve alloc]
                        initWithParameterID:CHHapticDynamicParameterIDHapticIntensityControl
                        controlPoints:points
                        relativeTime:0.0];

                NSError *error = nil;

                CHHapticPattern *pattern =
                    [[CHHapticPattern alloc]
                        initWithEvents:@[event]
                        parameterCurves:@[intensityCurve]
                        error:&error];

                if (error || !pattern)
                {
                    NSLog(@"MOST CoreHaptics pattern failed: %@", error);
                    return;
                }

                gCorePlayer = [gCoreEngine createPlayerWithPattern:pattern error:&error];

                if (error || !gCorePlayer)
                {
                    NSLog(@"MOST CoreHaptics player failed: %@", error);
                    return;
                }

                [gCorePlayer startAtTime:CHHapticTimeImmediate error:&error];

                if (error)
                    NSLog(@"MOST CoreHaptics playback failed: %@", error);
            }
        });
    }
}
