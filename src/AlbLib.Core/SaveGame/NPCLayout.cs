/* PORT: byte offsets of the NPC/character record, derived from the field order of
   NPC(BinaryReader) and Character(byte[]) in upstream AlbLib. */
namespace AlbLib.SaveGame
{
	/// <summary>
	/// Byte offsets of the fields in an NPC / party character record (little endian).
	/// </summary>
	public static class NPCLayout
	{
		public const int Type = 0;              // byte
		public const int Gender = 1;            // byte
		public const int Race = 2;              // byte
		public const int Class = 3;             // byte
		public const int Magic = 4;             // byte (flags)
		public const int Level = 5;             // byte
		// 6..7 unknown
		public const int Language = 8;          // byte (flags)
		public const int Appearance = 9;        // byte
		public const int Face = 10;             // byte
		public const int InventoryPicture = 11; // byte
		// 12..14 unknown
		public const int Morale = 15;           // byte 0..100 (PORT: UAlbion Morale)
		// 16 unknown
		public const int ActionPoints = 17;     // byte
		public const int EventSetId = 18;       // uint16 (PORT: upstream "DialogueOptions")
		public const int WordSetId = 20;        // uint16 (PORT: upstream "ResponseOptions")
		public const int TrainingPoints = 22;   // int16
		public const int Gold = 24;             // uint16, tenths of a gold piece (PORT: unsigned)
		public const int Rations = 26;          // int16
		// 28..29 unknown
		public const int Conditions = 30;       // int16 (flags)
		// 32..41 unknown
		// attributes/skills: int16 current, int16 max, then 4 unknown bytes each
		public const int Strength = 42;
		public const int Intelligence = 50;
		public const int Dexterity = 58;
		public const int Speed = 66;
		public const int Stamina = 74;
		public const int Luck = 82;
		public const int MagicResistance = 90;
		public const int MagicTalent = 98;
		public const int Age = 106;             // int16 current, int16 max, 4 bytes boost/backup (PORT: was a single int16)
		// 110..121 unknown
		public const int CloseRangeCombat = 122;
		public const int LongRangeCombat = 130;
		public const int CriticalHit = 138;
		public const int Lockpicking = 146;
		// 150..201 unknown
		public const int LifePoints = 202;      // int16 current, int16 max
		// 206..207 unknown
		public const int SpellPoints = 208;     // int16 current, int16 max
		public const int SpellPointsBoost = 212; // int16, preserved (PORT: upstream misread 212..219 as protection/damage)
		public const int BaseDefense = 214;     // uint16
		public const int BonusDefense = 216;    // int16
		public const int BaseAttack = 218;      // uint16
		public const int BonusAttack = 220;     // int16
		// 222..225 unknown
		public const int LevelsPerActionPoint = 226;   // uint16
		public const int LifePointsPerLevel = 228;     // uint16
		public const int SpellPointsPerLevel = 230;    // uint16
		// 232..233 unknown
		public const int TrainingPointsPerLevel = 234; // uint16
		// 236..237 unknown
		public const int Experience = 238;      // int32
		public const int Spells = 242;          // 7 x int32 known-spell bit masks
		// 270..273 unknown
		public const int Name = 274;            // 3 x 16 bytes (German, English, French), NUL terminated
		public const int NameSlotSize = 16;
		public const int SpellStrengths = 322;  // 7 x 30 x int16
		public const int Size = 742;            // end of NPC part
		public const int Equipment = 742;       // 9 x 6-byte item stacks
		public const int Backpack = 796;        // 24 x 6-byte item stacks
		public const int CharacterSize = 940;
	}
}
