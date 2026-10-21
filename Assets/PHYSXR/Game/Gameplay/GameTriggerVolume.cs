using System;
using UnityEngine;

namespace PHYSXR.Game.Gameplay
{
    // A trigger box that reports when the player's body enters it. Used for
    // the scripted beats (first sighting in the corridor) and the encounter
    // trigger at the dining-room doorway. It only reports; GameFlowController
    // decides what the crossing means and whether it still applies.
    [RequireComponent(typeof(BoxCollider))]
    public class GameTriggerVolume : MonoBehaviour
    {
        private Transform player;

        public string Id { get; private set; }

        public event Action<GameTriggerVolume> Entered;

        public void Initialize(string id, Transform player, Vector3 size)
        {
            Id = id;
            this.player = player;
            BoxCollider box = GetComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = size;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (player != null && other.transform == player)
                Entered?.Invoke(this);
        }
    }
}
