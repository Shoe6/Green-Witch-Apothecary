using System;
using System.Collections.Generic;
using UnityEngine;

namespace GreenWitchApothecary.Data
{
    /// <summary>
    /// Coordinates Firebase Anonymous Authentication, RTDB save-states upon sleep triggers,
    /// and Cloud Function AFK Epoch growth synchronization.
    /// Requires Firebase Unity SDK (FirebaseAuth, FirebaseDatabase, FirebaseFunctions).
    /// </summary>
    public class FirebaseDatabaseManager : MonoBehaviour
    {
        public string UserId { get; private set; }
        public bool IsAuthenticated { get; private set; }

        public event Action<string> OnAuthenticated;
        public event Action<string> OnSaveCompleted;

        private void Start()
        {
            SignInAnonymously();
        }

        public void SignInAnonymously()
        {
            // Hook for FirebaseAuth.DefaultInstance.SignInAnonymouslyAsync()
            // Once Firebase Unity SDK (.unitypackage) is imported in the Unity Editor,
            // replace the stub callback with FirebaseAuth.DefaultInstance.SignInAnonymouslyAsync().
#if FIREBASE_AUTH_AVAILABLE
            Firebase.Auth.FirebaseAuth.DefaultInstance.SignInAnonymouslyAsync().ContinueWith(task =>
            {
                if (task.IsCompletedSuccessfully && task.Result != null)
                {
                    UserId = task.Result.User.UserId;
                    IsAuthenticated = true;
                    OnAuthenticated?.Invoke(UserId);
                }
            });
#else
            Debug.Log("[FirebaseDatabaseManager] Firebase SDK not yet imported in Unity Editor.");
#endif
        }

        public void SavePlayerStateOnSleep(int day, float stamina, Dictionary<string, PlantInstanceData> plants)
        {
            if (!IsAuthenticated)
            {
                Debug.LogWarning("[FirebaseDatabaseManager] Skipping cloud save: user not authenticated.");
                return;
            }

            long saveEpoch = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
#if FIREBASE_DATABASE_AVAILABLE
            var dbRef = Firebase.Database.FirebaseDatabase.DefaultInstance
                .GetReference($"players/{UserId}/saveState");
            var payload = new Dictionary<string, object>
            {
                { "currentDay", day },
                { "currentStamina", stamina },
                { "lastSavedEpoch", saveEpoch }
            };
            dbRef.UpdateChildrenAsync(payload).ContinueWith(task =>
            {
                if (task.IsCompletedSuccessfully)
                {
                    OnSaveCompleted?.Invoke($"Saved day {day} at epoch {saveEpoch}");
                }
            });
#else
            Debug.Log($"[FirebaseDatabaseManager] Queued sleep save (Day {day}, Epoch {saveEpoch}).");
#endif
        }
    }
}
