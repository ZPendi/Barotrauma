﻿using Lidgren.Network;

namespace Barotrauma
{
    partial class CharacterInfo
    {
        partial void SpawnInventoryItemProjSpecific(Item item)
        {
            Entity.Spawner.CreateNetworkEvent(item, false);
        }

        public void ServerWrite(NetBuffer msg)
        {
            msg.Write(ID);
            msg.Write(Name);
            msg.Write(Gender == Gender.Female);
            msg.Write((byte)HeadSpriteId);
            if (Job != null)
            {
                msg.Write(Job.Prefab.Identifier);
                msg.Write((byte)Job.Skills.Count);
                foreach (Skill skill in Job.Skills)
                {
                    msg.Write(skill.Identifier);
                    msg.Write(skill.Level);
                }
            }
            else
            {
                msg.Write("");
            }
        }
    }
}