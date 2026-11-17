using System;
using UnityEngine;

namespace TWF
{
    // Recorded sound effects, several variations per event. Built by the editor from Assets/Sounds
    // (TWF/Build Sfx Bank); Sfx picks a random variation and falls back to its synthesized sound.
    public class SfxBank : ScriptableObject
    {
        [Serializable]
        public class Entry
        {
            public string key;
            public AudioClip[] clips;
        }

        public Entry[] entries = new Entry[0];
    }
}
