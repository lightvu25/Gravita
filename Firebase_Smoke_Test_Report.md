# Firebase Smoke Test Report

Project: `D:\UnityProject\Puzzle 2D` — Unity 6000.4.0f1
Test environment: Unity Editor (Windows, standalone/editor platform)
Method: cleared console → entered Play Mode → `FirebaseBootstrap` auto-spawned `FirebaseService`; state polled via `FirebaseService.Instance` and console log capture.

## 1. Firebase SDK
- Installed: Firebase Unity SDK **13.17.0** (Assets-based import under `Assets/Firebase/`). Runtime assemblies present: `Firebase.App`, `Firebase.Auth`, `Firebase.Analytics`, `Firebase.Database`, `Firebase.Firestore`, `Firebase.Storage`, `Firebase.Messaging`, `Firebase.Crashlytics`, `Firebase.RemoteConfig`, `Firebase.Installations`, `Firebase.AppCheck`. EDM4U resolver present (`Assets/ExternalDependencyManager/`). Android deps resolved into `mainTemplate.gradle` (`firebase-auth-unity:13.17.0`, `firebase-auth:24.2.0`, etc.) and `AndroidResolverDependencies.xml`.
- Initialization: **Verified.** `FirebaseBootstrap` (RuntimeInitializeOnLoadMethod.BeforeSceneLoad) spawned `FirebaseService`; `FirebaseApp.DefaultInstance` was created without exceptions.
- Dependencies: **Available.** `CheckAndFixDependenciesAsync()` completed past the dependency check (no "Dependencies unavailable" branch taken).
- Result: **Firebase SDK initializes successfully in the Editor.**

## 2. Firebase Authentication
- FirebaseAuth initialization: **Verified.** `FirebaseAuth.GetAuth(app)` succeeded; `FirebaseAuthService` constructed (`auth=True` at runtime).
- CurrentUser: **null** — no persisted session.
- Existing auth state: `FirebaseService.CurrentStatus = AuthFailed` (not `Ready`). Startup anonymous sign-in failed:
  `[FirebaseAuthService] Anonymous sign-in failed: This operation is restricted to administrators only.`
  (`AuthError.AdminRestrictedOperation` — the Anonymous provider is disabled in Firebase Console.) Service logged: `Anonymous auth failed — email login still available.`
- Result: **FirebaseAuth initializes and is functional, but no authenticated session could be established** (anonymous provider disabled; email/password path exists in `FirebaseAuthService` but was not exercised — credentials would need to be entered manually via `LoginUI`).

## 3. Google Sign-In
- Google Sign-In SDK/plugin: No third-party plugin (GoogleSignIn-unity, Play Games, etc.) — **by design**: implemented via Firebase Auth's built-in federated OAuth flow (`FederatedOAuthProvider` + `FirebaseAuth.SignInWithProviderAsync`), which ships inside `Firebase.Auth.dll` 13.17.0.
- Platform tested: Unity Editor (Windows desktop).
- Google provider configuration: **Cannot be confirmed enabled.** `google-services.json` (Android, `com.game.firebasedb`, project `puzzle-2d-9e9a7`) and `google-services-desktop.json` contain an **empty `oauth_client` array**. No `GoogleService-Info.plist` exists (no iOS app registered). Provider enablement itself is console-side and must be verified there.
- Login attempted: **Yes.** A "SIGN IN WITH GOOGLE" button was added to `LoginUI` (inside `SignInGroup`), wired to `IAuthService.SignInWithGoogleAsync()`. In Play Mode the button's `onClick` was invoked and the full code path executed: `LoginUI.OnGoogleLoginClicked` → `FirebaseAuthService.SignInWithGoogleAsync` → `auth.SignInWithProviderAsync(provider)` — the exception was caught, translated via `ToFriendlyMessage`, and surfaced on the status label.
- Login result: **Failed — `AuthError.Unimplemented`.** Status text showed `Sign-in failed (Unimplemented).` This is a **platform limitation** (layer I): the Firebase Unity SDK does not implement the web-redirect OAuth provider flow on the Windows desktop/Editor platform. The same code path is the documented flow on Android and iOS, where `SignInWithProviderAsync` opens a native OAuth tab — but that can only be verified on a device build.
- Firebase credential: Not created (call never reached credential exchange — rejected at the platform layer).
- Firebase UID: null.
- Email: null.
- Result: **BLOCKED on Editor (platform limitation).** Implementation and UI wiring are in place and verified end-to-end; actual Google authentication must be tested on an Android/iOS device build after the console items in §5 are done. If Editor-side Google login is ever needed, the alternative is a Google Sign-In plugin that acquires an ID token + `GoogleAuthProvider.GetCredential` — not pursued per scope.

## 4. Errors

- Exact error: `[FirebaseAuthService] Anonymous sign-in failed: This operation is restricted to administrators only.`
  - Layer: B / G — Firebase Auth configuration / Firebase Console provider configuration.
  - Cause: The Anonymous sign-in provider is disabled in Firebase Console for project `puzzle-2d-9e9a7`. (Firebase Console-side, not Unity-side — the Unity call is correct.)
  - Required fix: Firebase Console → Authentication → Sign-in method → enable **Anonymous**. Then `FirebaseService` will reach `Ready`.

- Exact error: `Sign-in failed (Unimplemented).` (`AuthError.Unimplemented` from `SignInWithProviderAsync` on desktop)
  - Layer: I — platform limitation.
  - Cause: Firebase Unity SDK does not implement the federated OAuth web flow on Windows Editor/desktop. Unity-side limitation, not a code bug — the error was surfaced cleanly through `AuthResult`.
  - Required fix: none in code. Test on an Android/iOS device build where the flow is supported (after console setup).

- Exact error: `Database URL not set in the Firebase config.`
  - Layer: E — google-services.json / platform config.
  - Cause: Neither `google-services.json` nor `google-services-desktop.json` contains a `firebase_url`/Realtime Database URL. (Note: `FirebaseCloudSaveProvider` actually uses **Cloud Firestore**, not Realtime DB — Firestore keying is project-scoped and unaffected by this warning; the warning only matters if Realtime DB is added later.)
  - Required fix: none needed for the current Firestore-based cloud save; only if a Realtime Database is added.

## 5. Firebase Console Requirements

The following must be done or verified in Firebase Console for project `puzzle-2d-9e9a7` — none of this can be confirmed as complete from project files:

- **Authentication → Sign-in providers → Google: needs to be enabled** (evidence: `oauth_client` array is empty — no OAuth clients exist for this app).
- **Authentication → Sign-in providers → Anonymous: needs to be enabled** (verified disabled at runtime — `AdminRestrictedOperation`).
- **Android app `com.game.firebasedb`: SHA-1 (and SHA-256) certificate fingerprint should be registered** before device testing — the OAuth flow can behave inconsistently without it. Re-download `google-services.json` afterward and confirm `oauth_client` is populated.
- **iOS: no app registered** — `GoogleService-Info.plist` is absent. If iOS is a target, an iOS app must be registered in the console and the plist added to the project.
- **Cloud Firestore**: enable the database (needed later for `FirebaseCloudSaveProvider` / account save — not part of this test).

## 6. Code Changes
- Files changed:
  - `Assets/Scripts/Firebase/IAuthService.cs` — added `Task<AuthResult> SignInWithGoogleAsync()` to the interface.
  - `Assets/Scripts/Firebase/FirebaseAuthService.cs` — implemented `SignInWithGoogleAsync` via `FederatedOAuthProvider("google.com")` + `SignInWithProviderAsync`; added `AuthError.WebContextCancelled` → "Sign-in was cancelled." friendly message.
  - `Assets/Scripts/UI/Panels/LoginUI.cs` — added `googleLoginButton` serialized field, `OnGoogleLoginClicked` handler, and interactable toggling.
  - `Assets/Scenes/GameScene.unity` — added `GoogleLoginButton` (clone of Login button, label "SIGN IN WITH GOOGLE") inside `SignInGroup`, wired to the new field.
  - `AgentScripts/KM_GoogleButton.cs` — new one-shot editor utility that created/wired the button.
- Files not changed: `FirebaseService.cs`, `FirebaseBootstrap.cs`, `FirebaseCloudSaveProvider.cs`, `FirebaseSmokeTest.cs`, `SaveManager.cs`, `AuthState.cs`, `AuthResult.cs`. No gameplay systems, no cloud-save sync hook (deferred per user instruction). No credentials were invented or added.

## 7. Final Status

**PARTIAL**

- Verified working: Firebase SDK 13.17.0 installs, dependency check passes, `FirebaseApp` initializes, `FirebaseAuth` initializes, the existing `FirebaseService`/`FirebaseAuthService`/`FirebaseBootstrap` init chain executes end-to-end in the Editor.
- Verified working (new): the Google Sign-In code path and UI wiring — button click → `IAuthService` → `FirebaseAuth.SignInWithProviderAsync` → caught error → status label. The plumbing is proven end-to-end.
- Verified failing: session establishment — anonymous sign-in is rejected because the provider is disabled in Firebase Console (`AuthFailed`, not `Ready`); Google sign-in attempt returns `AuthError.Unimplemented` because the OAuth web flow is not supported on the Windows Editor platform.
- Blocked: actual Google authentication — requires (a) Google provider enabled in Firebase Console and (b) an Android/iOS device build to exercise the supported native OAuth flow. Neither is available in this environment.
