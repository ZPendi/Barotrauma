﻿using Microsoft.Xna.Framework;
using System.Xml.Linq;

namespace Barotrauma
{
    class DamageModifier
    {
        [Serialize(1.0f, false)]
        public float DamageMultiplier
        {
            get;
            private set;
        }
        
        [Serialize("0.0,360", false)]
        public Vector2 ArmorSector
        {
            get;
            private set;
        }

        [Serialize(true, false)]
        public bool IsArmor
        {
            get;
            private set;
        }

        [Serialize(false, false)]
        public bool DeflectProjectiles
        {
            get;
            private set;
        }

        public string[] AfflictionNames
        {
            get;
            private set;
        }

        public string[] AfflictionTypes
        {
            get;
            private set;
        }



#if CLIENT
        [Serialize("", false)]
        public string DamageSound
        {
            get;
            private set;
        }
#endif

        public DamageModifier(XElement element)
        {
            SerializableProperty.DeserializeProperties(this, element);
            ArmorSector = new Vector2(MathHelper.ToRadians(ArmorSector.X), MathHelper.ToRadians(ArmorSector.Y));

            AfflictionNames = element.GetAttributeStringArray("afflictionnames", new string[0]);
            for (int i = 0; i < AfflictionNames.Length; i++)
            {
                AfflictionNames[i] = AfflictionNames[i].ToLowerInvariant();
            }
            AfflictionTypes = element.GetAttributeStringArray("afflictiontypes", new string[0]);
            for (int i = 0; i < AfflictionTypes.Length; i++)
            {
                AfflictionTypes[i] = AfflictionTypes[i].ToLowerInvariant();
            }
        }

        public bool MatchesAffliction(Affliction affliction)
        {
            foreach (string afflictionName in AfflictionNames)
            {
                if (affliction.Prefab.Name.ToLowerInvariant() == afflictionName) return true;
            }
            foreach (string afflictionType in AfflictionTypes)
            {
                if (affliction.Prefab.AfflictionType.ToLowerInvariant() == afflictionType) return true;
            }
            return false;
        }
    }
}