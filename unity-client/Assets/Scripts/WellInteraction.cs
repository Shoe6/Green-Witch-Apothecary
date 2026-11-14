using GreenWitchApothecary.Core;
using UnityEngine;

namespace GreenWitchApothecary.World
{
    /// <summary>
    /// Provides zero-stamina "Drink Water" interaction to prevent player softlocks.
    /// </summary>
    public class WellInteraction : MonoBehaviour
    {
        [SerializeField] private GameStateManager gameStateManager;

        public void OnDrinkWaterAction()
        {
            if (gameStateManager == null)
            {
                gameStateManager = FindFirstObjectByType<GameStateManager>();
            }
            gameStateManager?.DrinkWaterFromWell();
        }
    }
}
