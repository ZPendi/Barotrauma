using Barotrauma.Extensions;
﻿using Barotrauma.Items.Components;
using Barotrauma.Networking;
using Lidgren.Network;
using Microsoft.Xna.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace Barotrauma
{
    public enum Gender { None, Male, Female };
    public enum Race { None, White, Black, Asian };
    
    partial class CharacterInfo
    {
        private static Dictionary<string, XDocument> cachedConfigs = new Dictionary<string, XDocument>();

        private static ushort idCounter;

        public string Name;
        public string DisplayName
        {
            get
            {
                string disguiseName = "?";
                if (Character == null || !Character.HideFace || (GameMain.Server != null && !GameMain.Server.AllowDisguises))
                {
                    return Name;
                }
#if CLIENT
                if (GameMain.Client != null && !GameMain.Client.AllowDisguises)
                {
                    return Name;
                }
#endif
                if (Character.Inventory != null)
                {
                    int cardSlotIndex = Character.Inventory.FindLimbSlot(InvSlotType.Card);
                    if (cardSlotIndex < 0) return disguiseName;

                    var idCard = Character.Inventory.Items[cardSlotIndex];
                    if (idCard == null) return disguiseName;

                    //Disguise as the ID card name if it's equipped                    
                    string[] readTags = idCard.Tags.Split(',');
                    foreach (string tag in readTags)
                    {
                        string[] s = tag.Split(':');
                        if (s[0] == "name")
                        {
                            return s[1];
                        }
                    }
                }
                return disguiseName;
            }
        }

        /// <summary>
        /// Note: Can be null.
        /// </summary>
        public Character Character;

        public readonly string File;
        
        public Job Job;
        
        public int Salary;

        private Vector2 headSpriteRange;

        private Sprite headSprite;
        public Sprite HeadSprite
        {
            get
            {
                if (headSprite == null)
                {
                    LoadHeadSprite();
                }
                return headSprite;
            }
        }

        private Sprite portrait;
        public Sprite Portrait
        {
            get
            {
                if (portrait == null)
                {
                    LoadHeadSprite();
                }
                return portrait;
            }
        }

        private Sprite clothingSprite;
        public Sprite ClothingSprite
        {
            get
            {
                if (clothingSprite == null)
                {
                    if (Job != null && Job.Prefab.ClothingElement != null)
                    {
                        clothingSprite = new Sprite(Job.Prefab.ClothingElement.Element("sprite"));
                    }
                }
                return clothingSprite;
            }
        }

        private Sprite portraitBackground;
        public Sprite PortraitBackground
        {
            get
            {
                if (portraitBackground == null)
                {
                    var portraitBackgroundElement = SourceElement.Element("portraitbackground");
                    if (portraitBackgroundElement != null)
                    {
                        portraitBackground = new Sprite(portraitBackgroundElement.Element("sprite"));
                    }
                }
                return portraitBackground;
            }
        }

        private List<WearableSprite> attachmentSprites;
        public List<WearableSprite> AttachmentsSprites
        {
            get
            {
                if (attachmentSprites == null)
                {
                    LoadAttachmentSprites();
                }
                return attachmentSprites;
            }
        }

        public XElement SourceElement { get; set; }

        public XElement HairElement { get; private set; }
        public XElement BeardElement { get; private set; }
        public XElement MoustacheElement { get; private set; }
        public XElement FaceAttachment { get; private set; }

        public int HairIndex { get; set; } = -1;
        public int BeardIndex { get; set; } = -1;
        public int MoustacheIndex { get; set; } = -1;
        public int FaceAttachmentIndex { get; set; } = -1;

        public bool IsAttachmentsLoaded => HairIndex > -1 && BeardIndex > -1 && MoustacheIndex > -1 && FaceAttachmentIndex > -1;

        public readonly string ragdollFileName = string.Empty;

        public bool StartItemsGiven;

        public CauseOfDeath CauseOfDeath;

        public byte TeamID;

        private NPCPersonalityTrait personalityTrait;

        //unique ID given to character infos in MP
        //used by clients to identify which infos are the same to prevent duplicate characters in round summary
        public ushort ID;

        public XElement InventoryData;
               

        public List<string> SpriteTags
        {
            get;
            private set;
        }

        public NPCPersonalityTrait PersonalityTrait
        {
            get { return personalityTrait; }
        }

        private int headSpriteId;
        public int HeadSpriteId
        {
            get { return headSpriteId; }
            set
            {
                int oldId = headSpriteId;

                headSpriteId = value;
                Vector2 spriteRange = headSpriteRange;
                
                if (headSpriteId < (int)spriteRange.X) headSpriteId = (int)(spriteRange.Y);
                if (headSpriteId > (int)spriteRange.Y) headSpriteId = (int)(spriteRange.X);

                headSprite = null;
                attachmentSprites = null;
                ResetHeadAttachments();
            }
        }

        private Gender gender;
        public Gender Gender
        {
            get { return gender; }
            set
            {
                if (gender == value) return;
                gender = value;
                if (gender == Gender.None)
                {
                    Gender = Gender.Male;
                    //SetRandomGender();
                }
                CalculateHeadSpriteRange();
                ResetHeadAttachments();
                headSprite = null;
                attachmentSprites = null;
                //SetRandomHead();
                //LoadHeadSprite();
            }
        }

        private Race race;
        public Race Race
        {
            get { return race; }
            set
            {
                if (race == value) { return; }
                race = value;
                if (race == Race.None)
                {
                    race = Race.White;
                    //SetRandomRace();
                }
                CalculateHeadSpriteRange();
                ResetHeadAttachments();
                headSprite = null;
                attachmentSprites = null;
                //SetRandomHead();
                //LoadHeadSprite();
            }
        }

        private RagdollParams ragdoll;
        public RagdollParams Ragdoll
        {
            get
            {
                if (ragdoll == null)
                {
                    string speciesName = SourceElement.GetAttributeString("name", string.Empty);
                    bool isHumanoid = SourceElement.GetAttributeBool("humanoid", false);
                    ragdoll = isHumanoid 
                        ? HumanRagdollParams.GetRagdollParams(speciesName, ragdollFileName)
                        : RagdollParams.GetRagdollParams<FishRagdollParams>(speciesName, ragdollFileName) as RagdollParams;
                }
                return ragdoll;
            }
            set { ragdoll = value; }
        }

        // Used for creating the data
        public CharacterInfo(string file, string name = "", Gender gender = Gender.None, JobPrefab jobPrefab = null, string ragdollFileName = null)
        {
            ID = idCounter;
            idCounter++;
            File = file;
            SpriteTags = new List<string>();
            XDocument doc = GetConfig(file);
            SourceElement = doc.Root;
            if (doc.Root.GetAttributeBool("genders", false))
            {
                this.gender = gender == Gender.None ? GetRandomGender() : gender;
            }
            if (!Enum.TryParse(doc.Root.GetAttributeString("race", "None"), true, out race))
            {
                race = GetRandomRace();
            }
            if (race == Race.None)
            {
                race = GetRandomRace();
            }
            CalculateHeadSpriteRange();
            SetRandomHeadID();
            Job = (jobPrefab == null) ? Job.Random(Rand.RandSync.Server) : new Job(jobPrefab);
            if (!string.IsNullOrEmpty(name))
            {
                Name = name;
            }
            else
            {
                name = "";
                if (doc.Root.Element("name") != null)
                {
                    string firstNamePath = doc.Root.Element("name").GetAttributeString("firstname", "");
                    if (firstNamePath != "")
                    {
                        firstNamePath = firstNamePath.Replace("[GENDER]", (this.gender == Gender.Female) ? "female" : "male");
                        Name = ToolBox.GetRandomLine(firstNamePath);
                    }

                    string lastNamePath = doc.Root.Element("name").GetAttributeString("lastname", "");
                    if (lastNamePath != "")
                    {
                        lastNamePath = lastNamePath.Replace("[GENDER]", (this.gender == Gender.Female) ? "female" : "male");
                        if (Name != "") Name += " ";
                        Name += ToolBox.GetRandomLine(lastNamePath);
                    }
                }
            }
            personalityTrait = NPCPersonalityTrait.GetRandom(name + HeadSpriteId);         
            Salary = CalculateSalary();
            if (ragdollFileName != null)
            {
                this.ragdollFileName = ragdollFileName;
            }
            LoadHeadAttachments();
        }

        // Used for loading the data
        public CharacterInfo(XElement element)
        {
            ID = idCounter;
            idCounter++;
            Name = element.GetAttributeString("name", "unnamed");
            string genderStr = element.GetAttributeString("gender", "male").ToLowerInvariant();
            gender = (genderStr == "male") ? Gender.Male : Gender.Female;
            Enum.TryParse(element.GetAttributeString("race", "white"), true, out race);
            File = element.GetAttributeString("file", "");
            SourceElement = GetConfig(File).Root;
            Salary = element.GetAttributeInt("salary", 1000);
            headSpriteId = element.GetAttributeInt("headspriteid", 1);
            HairIndex = element.GetAttributeInt("hairindex", -1);
            BeardIndex = element.GetAttributeInt("beardindex", -1);
            MoustacheIndex = element.GetAttributeInt("moustacheindex", -1);
            FaceAttachmentIndex = element.GetAttributeInt("faceattachmentindex", -1);
            StartItemsGiven = element.GetAttributeBool("startitemsgiven", false);
            string personalityName = element.GetAttributeString("personality", "");
            ragdollFileName = element.GetAttributeString("ragdoll", string.Empty);
            if (!string.IsNullOrEmpty(personalityName))
            {
                personalityTrait = NPCPersonalityTrait.List.Find(p => p.Name == personalityName);
            }      
            foreach (XElement subElement in element.Elements())
            {
                if (subElement.Name.ToString().ToLowerInvariant() != "job") continue;
                Job = new Job(subElement);
                break;
            }
            LoadHeadAttachments();
        }

        public void LoadHeadSprite()
        {
            foreach (XElement limbElement in Ragdoll.MainElement.Elements())
            {
                if (limbElement.GetAttributeString("type", "").ToLowerInvariant() != "head") continue;

                XElement spriteElement = limbElement.Element("sprite");

                string spritePath = spriteElement.Attribute("texture").Value;

                spritePath = spritePath.Replace("[GENDER]", (gender == Gender.Female) ? "female" : "male");
                spritePath = spritePath.Replace("[RACE]", race.ToString().ToLowerInvariant());
                spritePath = spritePath.Replace("[HEADID]", HeadSpriteId.ToString());
                
                string fileName = Path.GetFileNameWithoutExtension(spritePath);

                //go through the files in the directory to find a matching sprite
                foreach (string file in Directory.GetFiles(Path.GetDirectoryName(spritePath)))
                {
                    if (!file.EndsWith(".png", StringComparison.InvariantCultureIgnoreCase))
                    {
                        continue;
                    }
                    string fileWithoutTags = Path.GetFileNameWithoutExtension(file);
                    fileWithoutTags = fileWithoutTags.Split('[', ']').First();
                    if (fileWithoutTags != fileName) continue;

                    headSprite = new Sprite(spriteElement, "", file);
                    portrait = new Sprite(spriteElement, "", file) { RelativeOrigin = Vector2.Zero };

                    //extract the tags out of the filename
                    SpriteTags = file.Split('[', ']').Skip(1).ToList();
                    if (SpriteTags.Any())
                    {
                        SpriteTags.RemoveAt(SpriteTags.Count-1);
                    }

                    break;                    
                }

                break;
            }
        }

        private XDocument GetConfig(string file)
        {
            if (!cachedConfigs.TryGetValue(file, out XDocument doc))
            {
                doc = XMLExtensions.TryLoadXml(file);
                if (doc == null) { return null; }
                cachedConfigs.Add(file, doc);
            }
            return doc;
        }

        public Gender SetRandomGender() => Gender = GetRandomGender();
        public Race SetRandomRace() => Race = GetRandomRace();
        public int SetRandomHead() => HeadSpriteId = SetRandomHeadID();
        public Gender GetRandomGender() => (Rand.Range(0.0f, 1.0f, Rand.RandSync.Server) < SourceElement.GetAttributeFloat("femaleratio", 0.5f)) ? Gender.Female : Gender.Male;
        public Race GetRandomRace() => new Race[] { Race.White, Race.Black, Race.Asian }.GetRandom(Rand.RandSync.Server);

        private int SetRandomHeadID()
        {
            if (headSpriteRange != Vector2.Zero)
            {
                headSpriteId = Rand.Range((int)headSpriteRange.X, (int)headSpriteRange.Y + 1, Rand.RandSync.Server);
            }
            else
            {
                headSpriteId = 0;
            }
            return headSpriteId;
        }

        private List<XElement> hairs;
        private List<XElement> beards;
        private List<XElement> moustaches;
        private List<XElement> faceAttachments;

        private IEnumerable<XElement> wearables;
        private IEnumerable<XElement> Wearables
        {
            get
            {
                if (wearables == null)
                {
                    var attachments = SourceElement.Element("HeadAttachments");
                    if (attachments != null)
                    {
                        wearables = attachments.Elements("Wearable");
                    }
                }
                return wearables;
            }
        }

        private IEnumerable<XElement> FilterElementsByGenderAndRace(IEnumerable<XElement> elements)
        {
            if (elements == null) { return elements; }
            return elements.Where(w =>
                Enum.TryParse(w.GetAttributeString("gender", "male"), true, out Gender g) && g == gender &&
                Enum.TryParse(w.GetAttributeString("race", "None"), true, out Race r) && r == race);
        }

        private void CalculateHeadSpriteRange()
        {
            if (SourceElement == null) { return; }
            headSpriteRange = SourceElement.GetAttributeVector2("headidrange", Vector2.Zero);
            if (headSpriteRange == Vector2.Zero)
            {
                // If range is defined, we use it as it is
                // Else we calculate the range from the wearables.
                var wearables = FilterElementsByGenderAndRace(Wearables);
                if (wearables == null)
                {
                    headSpriteRange = Vector2.Zero;
                    return;
                }
                if (wearables.None())
                {
                    DebugConsole.ThrowError($"[CharacterInfo] No headidrange defined and no wearables matching the gender {gender} and the race {race} could be found. Total wearables found: {Wearables.Count()}.");
                    return;
                }
                else
                {
                    // Ignore head ids that are less than 1, because they are not supported.
                    var ids = wearables.Select(w => w.GetAttributeInt("headid", -1)).Where(id => id > 0);
                    if (ids.None())
                    {
                        DebugConsole.ThrowError($"[CharacterInfo] Wearables with matching gender and race were found but none with a valid headid! Total wearables found: {Wearables.Count()}.");
                        return;
                    }
                    ids = ids.OrderBy(id => id);
                    headSpriteRange = new Vector2(ids.First(), ids.Last());
                }
            }
        }

        /// <summary>
        /// Loads only the elements according to the indices, not the sprites.
        /// </summary>
        public void LoadHeadAttachments()
        {
            if (Wearables != null)
            {
                if (hairs == null)
                {
                    hairs = AddEmpty(FilterByTypeAndHeadID(FilterElementsByGenderAndRace(wearables), WearableType.Hair), WearableType.Hair);
                }
                if (beards == null)
                {
                    beards = AddEmpty(FilterByTypeAndHeadID(FilterElementsByGenderAndRace(wearables), WearableType.Beard), WearableType.Beard);
                }
                if (moustaches == null)
                {
                    moustaches = AddEmpty(FilterByTypeAndHeadID(FilterElementsByGenderAndRace(wearables), WearableType.Moustache), WearableType.Moustache);
                }
                if (faceAttachments == null)
                {
                    faceAttachments = AddEmpty(FilterByTypeAndHeadID(FilterElementsByGenderAndRace(wearables), WearableType.FaceAttachment), WearableType.FaceAttachment);
                }

                if (IsValidIndex(HairIndex, hairs))
                {
                    HairElement = hairs[HairIndex];
                }
                else
                {
                    HairElement = GetRandomElement(hairs);
                    HairIndex = hairs.IndexOf(HairElement);
                }
                if (IsValidIndex(BeardIndex, beards))
                {
                    BeardElement = beards[BeardIndex];
                }
                else
                {
                    BeardElement = GetRandomElement(beards);
                    BeardIndex = beards.IndexOf(BeardElement);
                }
                if (IsValidIndex(MoustacheIndex, moustaches))
                {
                    MoustacheElement = moustaches[MoustacheIndex];
                }
                else
                {
                    MoustacheElement = GetRandomElement(moustaches);
                    MoustacheIndex = moustaches.IndexOf(MoustacheElement);
                }
                if (IsValidIndex(FaceAttachmentIndex, faceAttachments))
                {
                    FaceAttachment = faceAttachments[FaceAttachmentIndex];
                }
                else
                {
                    FaceAttachment = GetRandomElement(faceAttachments);
                    FaceAttachmentIndex = faceAttachments.IndexOf(FaceAttachment);
                }

                List<XElement> AddEmpty(IEnumerable<XElement> elements, WearableType type)
                {
                    // Let's add an empty element so that there's a chance that we don't get any actual element -> allows bald and beardless guys, for example.
                    var emptyElement = new XElement("EmptyWearable", type.ToString());
                    var list = new List<XElement>() { emptyElement };
                    list.AddRange(elements);
                    return list;
                }

                XElement GetRandomElement(IEnumerable<XElement> elements)
                {
                    var filtered = elements.Where(e => IsWearableAllowed(e)).ToList();
                    if (filtered.Count == 0) { return null; }
                    var weights = GetWeights(filtered).ToList();
                    var element = ToolBox.SelectWeightedRandom(filtered, weights, Rand.RandSync.Server);
                    return element == null || element.Name == "Empty" ? null : element;
                }

                IEnumerable<XElement> FilterByTypeAndHeadID(IEnumerable<XElement> elements, WearableType targetType)
                {
                    return elements.Where(e =>
                    {
                        if (Enum.TryParse(e.GetAttributeString("type", ""), true, out WearableType type) && type != targetType) { return false; }
                        int headId = e.GetAttributeInt("headid", -1);
                        // if the head id is less than 1, the id is not valid and the condition is ignored.
                        return headId < 1 || headId == headSpriteId;
                    });
                }

                bool IsWearableAllowed(XElement element)
                {
                    string spriteName = element.Element("sprite").GetAttributeString("name", string.Empty);
                    return IsAllowed(HairElement, spriteName) && IsAllowed(BeardElement, spriteName) && IsAllowed(MoustacheElement, spriteName) && IsAllowed(FaceAttachment, spriteName);
                }

                bool IsAllowed(XElement element, string spriteName)
                {
                    if (element != null)
                    {
                        var disallowed = element.GetAttributeStringArray("disallow", new string[0]);
                        if (disallowed.Any(s => spriteName.Contains(s)))
                        {
                            return false;
                        }
                    }
                    return true;
                }

                bool IsValidIndex(int index, List<XElement> list) => index >= 0 && index < list.Count;
                IEnumerable<float> GetWeights(IEnumerable<XElement> elements) => elements.Select(h => h.GetAttributeFloat("commonness", 1f));
            }
        }

        partial void LoadAttachmentSprites();
        
        private int CalculateSalary()
        {
            if (Name == null || Job == null) return 0;

            int salary = Math.Abs(Name.GetHashCode()) % 100;

            foreach (Skill skill in Job.Skills)
            {
                salary += (int)skill.Level * 50;
            }

            return salary;
        }

        public void IncreaseSkillLevel(string skillIdentifier, float increase, Vector2 worldPos)
        {
            if (Job == null || GameMain.Client != null) return;            

            float prevLevel = Job.GetSkillLevel(skillIdentifier);
            Job.IncreaseSkillLevel(skillIdentifier, increase);

            float newLevel = Job.GetSkillLevel(skillIdentifier);

            OnSkillChanged(skillIdentifier, prevLevel, newLevel, worldPos);

            if (GameMain.Server != null && (int)newLevel != (int)prevLevel)
            {
                GameMain.Server.CreateEntityEvent(Character, new object[] { NetEntityEvent.Type.UpdateSkills });                
            }
        }

        public void SetSkillLevel(string skillIdentifier, float level, Vector2 worldPos)
        {
            if (Job == null) return;

            var skill = Job.Skills.Find(s => s.Identifier == skillIdentifier);
            if (skill == null)
            {
                Job.Skills.Add(new Skill(skillIdentifier, level));
                OnSkillChanged(skillIdentifier, 0.0f, skill.Level, worldPos);
            }
            else
            {
                float prevLevel = skill.Level;
                skill.Level = level;
                OnSkillChanged(skillIdentifier, prevLevel, skill.Level, worldPos);
            }
        }

        partial void OnSkillChanged(string skillIdentifier, float prevLevel, float newLevel, Vector2 textPopupPos);

        public virtual XElement Save(XElement parentElement)
        {
            XElement charElement = new XElement("Character");

            charElement.Add(
                new XAttribute("name", Name),
                new XAttribute("file", File),
                new XAttribute("gender", gender == Gender.Male ? "male" : "female"),
                new XAttribute("race", race.ToString()),
                new XAttribute("salary", Salary),
                new XAttribute("headspriteid", HeadSpriteId),
                new XAttribute("hairindex", HairIndex),
                new XAttribute("beardindex", BeardIndex),
                new XAttribute("moustacheindex", MoustacheIndex),
                new XAttribute("faceattachmentindex", FaceAttachmentIndex),
                new XAttribute("startitemsgiven", StartItemsGiven),
                new XAttribute("ragdoll", ragdollFileName),
                new XAttribute("personality", personalityTrait == null ? "" : personalityTrait.Name));
            
            // TODO: animations?

            if (Character != null)
            {
                if (Character.AnimController.CurrentHull != null)
                {
                    charElement.Add(new XAttribute("hull", Character.AnimController.CurrentHull.ID));
                }
            }
            
            Job.Save(charElement);

            parentElement.Add(charElement);
            return charElement;
        }

        public void SpawnInventoryItems(Inventory inventory, XElement itemData)
        {
            SpawnInventoryItemsRecursive(inventory, itemData);
        }

        private void SpawnInventoryItemsRecursive(Inventory inventory, XElement element)
        {
            foreach (XElement itemElement in element.Elements())
            {
                var newItem = Item.Load(itemElement, inventory.Owner.Submarine);
                int slotIndex = itemElement.GetAttributeInt("i", 0);
                if (newItem == null) continue;

                Entity.Spawner.CreateNetworkEvent(newItem, false);

                inventory.TryPutItem(newItem, slotIndex, false, false, null);

                int itemContainerIndex = 0;
                var itemContainers = newItem.GetComponents<ItemContainer>().ToList();
                foreach (XElement childInvElement in itemElement.Elements())
                {
                    if (itemContainerIndex >= itemContainers.Count) break;
                    if (childInvElement.Name.ToString().ToLowerInvariant() != "inventory") continue;
                    SpawnInventoryItemsRecursive(itemContainers[itemContainerIndex].Inventory, childInvElement);
                    itemContainerIndex++;
                }
            }
        }

        public void ServerWrite(NetBuffer msg)
        {
            msg.Write(ID);
            msg.Write(Name);
            msg.Write(Gender == Gender.Female);
            msg.Write((byte)Race);
            msg.Write((byte)HeadSpriteId);
            msg.Write((byte)HairIndex);
            msg.Write((byte)BeardIndex);
            msg.Write((byte)MoustacheIndex);
            msg.Write((byte)FaceAttachmentIndex);
            msg.Write(ragdollFileName);

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
            // TODO: animations
        }

        public static CharacterInfo ClientRead(string configPath, NetBuffer inc)
        {
            ushort infoID               = inc.ReadUInt16();
            string newName              = inc.ReadString();
            bool isFemale               = inc.ReadBoolean();
            int race                    = inc.ReadByte();
            int headSpriteID            = inc.ReadByte();
            int hairIndex               = inc.ReadByte();
            int beardIndex              = inc.ReadByte();
            int moustacheIndex          = inc.ReadByte();
            int faceAttachmentIndex     = inc.ReadByte();
            string ragdollFile          = inc.ReadString();

            string jobIdentifier        = inc.ReadString();
            JobPrefab jobPrefab = null;
            Dictionary<string, float> skillLevels = new Dictionary<string, float>();
            if (!string.IsNullOrEmpty(jobIdentifier))
            {
                jobPrefab = JobPrefab.List.Find(jp => jp.Identifier == jobIdentifier);
                int skillCount = inc.ReadByte();
                for (int i = 0; i < skillCount; i++)
                {
                    string skillIdentifier = inc.ReadString();
                    float skillLevel = inc.ReadSingle();
                    skillLevels.Add(skillIdentifier, skillLevel);
                }
            }

            // TODO: animations

            CharacterInfo ch = new CharacterInfo(configPath, newName, isFemale ? Gender.Female : Gender.Male, jobPrefab, ragdollFile)
            {
                ID = infoID,
                race = (Race)race,
                headSpriteId = headSpriteID,
                HairIndex = hairIndex,
                BeardIndex = beardIndex,
                MoustacheIndex = moustacheIndex,
                FaceAttachmentIndex = faceAttachmentIndex
            };
            ch.CalculateHeadSpriteRange();
            ch.ReloadHeadAttachments();

            System.Diagnostics.Debug.Assert(skillLevels.Count == ch.Job.Skills.Count);
            if (ch.Job != null)
            {
                foreach (KeyValuePair<string, float> skill in skillLevels)
                {
                    Skill matchingSkill = ch.Job.Skills.Find(s => s.Identifier == skill.Key);
                    if (matchingSkill == null)
                    {
                        DebugConsole.ThrowError("Skill \"" + skill.Key + "\" not found in character \"" + newName + "\"");
                        continue;
                    }
                    matchingSkill.Level = skill.Value;
                }
            }
            return ch;
        }

        public void ReloadHeadAttachments()
        {
            ResetLoadedAttachments();
            LoadHeadAttachments();
        }

        public void ResetHeadAttachments()
        {
            ResetAttachmentIndices();
            ResetLoadedAttachments();
        }

        private void ResetAttachmentIndices()
        {
            HairIndex = -1;
            BeardIndex = -1;
            MoustacheIndex = -1;
            FaceAttachmentIndex = -1;
        }

        private void ResetLoadedAttachments()
        {
            hairs = null;
            beards = null;
            moustaches = null;
            faceAttachments = null;
        }

        public void Remove()
        {
            Character = null;
            if (headSprite != null)
            {
                headSprite.Remove();
                headSprite = null;
            }
            if (portrait != null)
            {
                portrait.Remove();
                portrait = null;
            }
            if (portraitBackground != null)
            {
                portraitBackground.Remove();
                portraitBackground = null;
            }
            if (attachmentSprites != null)
            {
                attachmentSprites.ForEach(a => a.Sprite.Remove());
                attachmentSprites = null;
            }
        }
    }
}
