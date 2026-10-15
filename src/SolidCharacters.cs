using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;

namespace SizeboxTweaks
{
    // Walking into another character shoved it away like it weighed nothing: the player
    // controller sets its velocity directly, and a character the same size has the same mass.
    // Raising NPC mass would change the game's crush maths, so instead only contacts between
    // the player and another character are changed: for those, the other one counts as
    // immovable. Everything else (giantess feet, NPC vs NPC, objects) is untouched.
    internal class SolidCharacters : MonoBehaviour
    {
        // Rigidbody instance ids, swapped as a whole so the physics thread never sees a half-built set
        private static volatile HashSet<int> _player = new HashSet<int>();
        private static volatile HashSet<int> _others = new HashSet<int>();
        private readonly HashSet<int> _modifiable = new HashSet<int>();
        private float _next;

        internal static void Install(GameObject host)
        {
            host.AddComponent<SolidCharacters>();
            Physics.ContactModifyEvent += Modify;
            Physics.ContactModifyEventCCD += Modify;
        }

        private void Update()
        {
            if (Time.unscaledTime < _next)
                return;
            _next = Time.unscaledTime + 0.5f;
            if (!Plugin.SolidCharacters.Value || GameController.LocalClient == null)
            {
                _player = new HashSet<int>();
                return;
            }
            EntityBase me = GameController.LocalClient.Player?.Entity;
            var player = new HashSet<int>();
            var others = new HashSet<int>();
            foreach (Humanoid h in FindObjectsOfType<Humanoid>())
            {
                Rigidbody rb = h.Rigidbody;
                if (rb == null)
                    continue;
                (h == me ? player : others).Add(rb.GetInstanceID());
                // Contacts are only reported for modification on colliders that ask for it
                if (_modifiable.Add(h.GetInstanceID()))
                    foreach (Collider c in h.GetComponentsInChildren<Collider>(true))
                        if (c.attachedRigidbody == rb)
                            c.hasModifiableContacts = true;
            }
            _player = player;
            _others = others;
        }

        // Runs on the physics thread: no Unity API calls here.
        private static void Modify(PhysicsScene scene, NativeArray<ModifiableContactPair> pairs)
        {
            HashSet<int> player = _player, others = _others;
            if (player.Count == 0)
                return;
            for (int i = 0; i < pairs.Length; i++)
            {
                ModifiableContactPair pair = pairs[i];
                int a = pair.bodyInstanceID, b = pair.otherBodyInstanceID;
                ModifiableMassProperties m = pair.massProperties;
                if (player.Contains(a) && others.Contains(b))
                {
                    m.otherInverseMassScale = 0f;
                    m.otherInverseInertiaScale = 0f;
                }
                else if (player.Contains(b) && others.Contains(a))
                {
                    m.inverseMassScale = 0f;
                    m.inverseInertiaScale = 0f;
                }
                else
                    continue;
                pair.massProperties = m;
                pairs[i] = pair;
            }
        }
    }
}
