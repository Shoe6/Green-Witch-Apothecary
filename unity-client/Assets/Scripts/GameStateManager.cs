using System;
using UnityEngine;

namespace GreenWitchApothecary.Core
{
    /// <summary>
    /// Manages the 6:00 AM to 12:00 AM day/night cycle, stamina economy,
    /// Midnight Pass-Out rescue sequence, and zero-cost well softlock protection.
    /// </summary>
    public class GameStateManager : MonoBehaviour
    {
        public const float DayStartMinutes = 6f * 60f;        // 6:00 AM (360)
        public const float MidnightMinutes = 24f * 60f;       // 12:00 AM (1440)
        public const float PassOutWakeMinutes = 10f * 60f;    // 10:00 AM (600)

        [Header("Clock Settings")]
        [Tooltip("In-game minutes advanced per real-world second.")]
        [SerializeField] private float gameMinutesPerRealSecond = 1.5f;

        [Header("Stamina Settings")]
        [SerializeField] private float maxStamina = 100f;
        [SerializeField] private float passOutWakeStaminaRatio = 0.65f;
        [SerializeField] private float wellMicroStaminaBoost = 12f;

        [Header("Day/Night Lighting")]
        [SerializeField] private Light sunLight;
        [SerializeField] private Gradient sunColorOverDay;
        [SerializeField] private AnimationCurve sunIntensityCurve = AnimationCurve.EaseInOut(0f, 0.1f, 1f, 1f);

        public int CurrentDay { get; private set; } = 1;
        public float CurrentTimeMinutes { get; private set; } = DayStartMinutes;
        public float CurrentStamina { get; private set; }
        public bool IsSleepingOrPassingOut { get; private set; }
        public string PendingBedsideNote { get; private set; }

        public event Action<int, float> OnTimeUpdated;
        public event Action<float, float> OnStaminaChanged;
        public event Action<string> OnMidnightPassOutTriggered;
        public event Action<int, float, string> OnDayStarted;

        private void Awake()
        {
            CurrentStamina = maxStamina;
        }

        private void Start()
        {
            OnDayStarted?.Invoke(CurrentDay, CurrentTimeMinutes, string.Empty);
            OnStaminaChanged?.Invoke(CurrentStamina, maxStamina);
            UpdateDaylightVisuals();
        }

        private void Update()
        {
            if (IsSleepingOrPassingOut) return;

            CurrentTimeMinutes += gameMinutesPerRealSecond * Time.deltaTime;
            if (CurrentTimeMinutes >= MidnightMinutes)
            {
                TriggerMidnightPassOut();
                return;
            }

            OnTimeUpdated?.Invoke(CurrentDay, CurrentTimeMinutes);
            UpdateDaylightVisuals();
        }

        public bool TryConsumeStamina(float amount)
        {
            if (amount <= 0f) return true;
            if (CurrentStamina < amount) return false;

            CurrentStamina = Mathf.Max(0f, CurrentStamina - amount);
            OnStaminaChanged?.Invoke(CurrentStamina, maxStamina);
            return true;
        }

        /// <summary>
        /// Zero-stamina action at the homestead well preventing save-state softlocks.
        /// </summary>
        public void DrinkWaterFromWell()
        {
            if (IsSleepingOrPassingOut) return;
            CurrentStamina = Mathf.Min(maxStamina, CurrentStamina + wellMicroStaminaBoost);
            OnStaminaChanged?.Invoke(CurrentStamina, maxStamina);
        }

        /// <summary>
        /// Voluntary sleep: advances to 6:00 AM next day with full stamina restoration.
        /// </summary>
        public void SleepInBed()
        {
            if (IsSleepingOrPassingOut) return;
            IsSleepingOrPassingOut = true;

            CurrentDay += 1;
            CurrentTimeMinutes = DayStartMinutes;
            CurrentStamina = maxStamina;
            PendingBedsideNote = string.Empty;

            IsSleepingOrPassingOut = false;
            UpdateDaylightVisuals();
            OnStaminaChanged?.Invoke(CurrentStamina, maxStamina);
            OnDayStarted?.Invoke(CurrentDay, CurrentTimeMinutes, PendingBedsideNote);
        }

        private void TriggerMidnightPassOut()
        {
            IsSleepingOrPassingOut = true;
            CurrentTimeMinutes = MidnightMinutes;

            const string note = "The Crow found you asleep near the garden beds at midnight. " +
                                "Warm tea was left by your kettle. — Barnaby, Village Miller";
            PendingBedsideNote = note;
            OnMidnightPassOutTriggered?.Invoke(note);

            // Advance to 10:00 AM next day with stamina penalty
            CurrentDay += 1;
            CurrentTimeMinutes = PassOutWakeMinutes;
            CurrentStamina = Mathf.Round(maxStamina * passOutWakeStaminaRatio);

            IsSleepingOrPassingOut = false;
            UpdateDaylightVisuals();
            OnStaminaChanged?.Invoke(CurrentStamina, maxStamina);
            OnDayStarted?.Invoke(CurrentDay, CurrentTimeMinutes, PendingBedsideNote);
        }

        private void UpdateDaylightVisuals()
        {
            if (sunLight == null) return;

            float normalizedDay = Mathf.InverseLerp(DayStartMinutes, MidnightMinutes, CurrentTimeMinutes);
            sunLight.intensity = sunIntensityCurve.Evaluate(normalizedDay);
            if (sunColorOverDay != null)
            {
                sunLight.color = sunColorOverDay.Evaluate(normalizedDay);
            }
            float sunPitch = Mathf.Lerp(15f, 195f, normalizedDay);
            sunLight.transform.rotation = Quaternion.Euler(sunPitch, -35f, 0f);
        }

        public string GetFormattedClock()
        {
            int totalMinutes = Mathf.FloorToInt(CurrentTimeMinutes) % 1440;
            int hours24 = totalMinutes / 60;
            int mins = totalMinutes % 60;
            string suffix = hours24 >= 12 ? "PM" : "AM";
            int hours12 = hours24 % 12;
            if (hours12 == 0) hours12 = 12;
            return $"{hours12:00}:{mins:00} {suffix}";
        }
    }
}
