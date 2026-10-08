using System;
using System.IO;
using System.Text;
using AlbLib.Texts;

namespace AlbLib.SaveGame
{
	/// <summary>
	/// Stores information about game NPC.
	/// </summary>
	[Serializable]
	public class NPC : IGameResource
	{
		public NPCType Type{get; set;}

		/// <summary>
		/// Gender of character.
		/// </summary>
		public Gender Gender{get; set;}

		/// <summary>
		/// Race of character.
		/// </summary>
		public Race Race{get; set;}

		/// <summary>
		/// Class of character.
		/// </summary>
		public CharacterClass Class{get; set;}

		/// <summary>
		/// Magic type of character.
		/// </summary>
		public MagicFlags Magic{get; set;}

		/// <summary>
		/// Level of character.
		/// </summary>
		public byte Level{get; set;}

		/// <summary>
		/// Speaking language of character.
		/// </summary>
		public LanguageFlags Language{get; set;}

		/// <summary>
		/// Character appearance.
		/// </summary>
		public byte Appearance{get; set;}

		/// <summary>
		/// Character face.
		/// </summary>
		public byte Face{get; set;}

		/// <summary>
		/// Character inventory picture.
		/// </summary>
		public byte InventoryPicture{get; set;}

		/// <summary>
		/// Actual character appearance.
		/// </summary>
		public PlayableCharacter NamedAppearance{
			get{return (PlayableCharacter)Appearance;}
			set{Appearance = (byte)value;}
		}

		/// <summary>
		/// Actual character face.
		/// </summary>
		public PlayableCharacter NamedFace{
			get{return (PlayableCharacter)Face;}
			set{Face = (byte)value;}
		}

		/// <summary>
		/// Actual character inventory picture.
		/// </summary>
		public PlayableCharacter NamedInventoryPicture{
			get{return (PlayableCharacter)InventoryPicture;}
			set{InventoryPicture = (byte)value;}
		}

		/// <summary>
		/// Combat action points.
		/// </summary>
		public byte ActionPoints{get; set;}

		/// <summary>
		/// PORT: event/script set link (UAlbion EventSetId). Upstream called this DialogueOptions (int16).
		/// </summary>
		public ushort EventSetId{get; set;}

		/// <summary>
		/// PORT: word set link (UAlbion WordSetId). Upstream called this ResponseOptions (int16).
		/// </summary>
		public ushort WordSetId{get; set;}

		/// <summary>
		/// PORT: morale 0..100 (offset 15, UAlbion Morale); was part of an unknown block upstream.
		/// </summary>
		public byte Morale{get; set;}

		/// <summary>
		/// Training points.
		/// </summary>
		public short TrainingPoints{get; set;}

		/// <summary>
		/// Gold in inventory.
		/// </summary>
		public decimal Gold{get; set;}

		/// <summary>
		/// Rations in inventory.
		/// </summary>
		public short Rations{get; set;}

		/// <summary>
		/// Character conditions.
		/// </summary>
		public ConditionFlags Conditions{get; set;}

		/// <summary>
		/// Character strength stat.
		/// </summary>
		public CharacterAttribute Strength{get; set;}

		/// <summary>
		/// Character intelligence stat.
		/// </summary>
		public CharacterAttribute Intelligence{get; set;}

		/// <summary>
		/// Character dexterity stat.
		/// </summary>
		public CharacterAttribute Dexterity{get; set;}

		/// <summary>
		/// Character speed stat.
		/// </summary>
		public CharacterAttribute Speed{get; set;}

		/// <summary>
		/// Character stamina stat.
		/// </summary>
		public CharacterAttribute Stamina{get; set;}

		/// <summary>
		/// Character luck stat.
		/// </summary>
		public CharacterAttribute Luck{get; set;}

		/// <summary>
		/// Character magic resistance stat.
		/// </summary>
		public CharacterAttribute MagicResistance{get; set;}

		/// <summary>
		/// Character magic talent stat.
		/// </summary>
		public CharacterAttribute MagicTalent{get; set;}

		/// <summary>
		/// Character close-range combat points.
		/// </summary>
		public CharacterAttribute CloseRangeCombat{get; set;}

		/// <summary>
		/// Character long-range combat points.
		/// </summary>
		public CharacterAttribute LongRangeCombat{get; set;}

		/// <summary>
		/// Character critical hit stat.
		/// </summary>
		public CharacterAttribute CriticalHit{get; set;}

		/// <summary>
		/// Character lockpicking stat.
		/// </summary>
		public CharacterAttribute Lockpicking{get; set;}

		/// <summary>
		/// Character life points.
		/// </summary>
		public CharacterAttribute LifePoints{get; set;}

		/// <summary>
		/// Character spell points.
		/// </summary>
		public CharacterAttribute SpellPoints{get; set;}

		/// <summary>
		/// Character age in years.
		/// PORT: age is a current/max pair (8-byte block like attributes); upstream read a single int16.
		/// </summary>
		public CharacterAttribute Age{get; set;}

		// PORT: upstream read BaseProtection/Protection/BaseDamage/Damage at 212..218, shifted by 2 bytes.
		// 212 is the spell point "boost" (kept as an unknown); the real fields start at 214 (UAlbion layout).
		/// <summary>PORT: base defense (offset 214, uint16).</summary>
		public ushort BaseDefense{get; set;}
		/// <summary>PORT: defense bonus from equipment (offset 216, int16).</summary>
		public short BonusDefense{get; set;}
		/// <summary>PORT: base attack (offset 218, uint16).</summary>
		public ushort BaseAttack{get; set;}
		/// <summary>PORT: attack bonus from equipment (offset 220, int16).</summary>
		public short BonusAttack{get; set;}
		/// <summary>PORT: growth per level (offset 226, uint16).</summary>
		public ushort LevelsPerActionPoint{get; set;}
		/// <summary>PORT: growth per level (offset 228, uint16).</summary>
		public ushort LifePointsPerLevel{get; set;}
		/// <summary>PORT: growth per level (offset 230, uint16).</summary>
		public ushort SpellPointsPerLevel{get; set;}
		/// <summary>PORT: growth per level (offset 234, uint16).</summary>
		public ushort TrainingPointsPerLevel{get; set;}

		/// <summary>
		/// Character experience points.
		/// </summary>
		public int Experience{get; set;}

		/// <summary>
		/// Character class-relative spells.
		/// </summary>
		public int[] Spells{get; private set;}

		/// <summary>
		/// Character name.
		/// </summary>
		public LanguageTerm Name{get; set;}

		public short[,] SpellStrengths{get; private set;}

		private readonly byte[][] unknown;

		/// <summary>PORT: original raw record (null when not loaded from a byte array).</summary>
		protected byte[] source;

		/// <summary>PORT: name as decoded on load; name slots are only rewritten when changed.</summary>
		private LanguageTerm originalName;

		/// <summary>
		/// Creates new character.
		/// </summary>
		public NPC()
		{
			Spells = new int[7];
			SpellStrengths = new short[7,30];
			unknown = new byte[24][];
			unknown[1] = new byte[2];
			unknown[2] = new byte[5];
			unknown[3] = new byte[2];
			unknown[4] = new byte[10];
			unknown[5] = new byte[4];
			unknown[6] = new byte[4];
			unknown[7] = new byte[4];
			unknown[8] = new byte[4];
			unknown[9] = new byte[4];
			unknown[10] = new byte[4];
			unknown[11] = new byte[4];
			unknown[12] = new byte[4];
			unknown[13] = new byte[12]; // PORT: 110..121 (age is now a 4-byte pair at 106)
			unknown[14] = new byte[4];
			unknown[15] = new byte[4];
			unknown[16] = new byte[4];
			unknown[17] = new byte[52];
			unknown[18] = new byte[2];
			unknown[19] = new byte[4];  // PORT: 222..225
			unknown[21] = new byte[2];  // PORT: 212 spell point boost
			unknown[22] = new byte[2];  // PORT: 232
			unknown[23] = new byte[2];  // PORT: 236
			unknown[20] = new byte[4];
		}

		/// <summary>
		/// Loads NPC.
		/// </summary>
		/// <param name="data">
		/// Byte array containing character data.
		/// </param>
		public NPC(byte[] data) : this(new MemoryStream(data, false))
		{
			// PORT: keep the original record so unknown bytes survive serialization.
			source = (byte[])data.Clone();
		}

		public NPC(Stream stream) : this(new BinaryReader(stream, TextCore.DefaultEncoding))
		{}

		public NPC(BinaryReader reader) : this()
		{
			Type = (NPCType)reader.ReadByte();
			Gender = (Gender)reader.ReadByte();
			Race = (Race)reader.ReadByte();
			Class = (CharacterClass)reader.ReadByte();
			Magic = (MagicFlags)reader.ReadByte();
			Level = reader.ReadByte();
			reader.Read(unknown[1], 0, 2);
			Language = (LanguageFlags)reader.ReadByte();
			Appearance = reader.ReadByte();
			Face = reader.ReadByte();
			InventoryPicture = reader.ReadByte();

			reader.Read(unknown[2], 0, 5);
			Morale = unknown[2][3]; // PORT: offset 15
			
			ActionPoints = reader.ReadByte();
			EventSetId = reader.ReadUInt16();
			WordSetId = reader.ReadUInt16();
			TrainingPoints = reader.ReadInt16();
			Gold = reader.ReadUInt16()/10M; // PORT: unsigned (upstream read int16)
			Rations = reader.ReadInt16();

			reader.Read(unknown[3], 0, 2);

			Conditions = (ConditionFlags)reader.ReadInt16();
			reader.Read(unknown[4], 0, 10);
			Strength = new CharacterAttribute(reader);
			reader.Read(unknown[5], 0, 4);
			Intelligence = new CharacterAttribute(reader);
			reader.Read(unknown[6], 0, 4);
			Dexterity = new CharacterAttribute(reader);
			reader.Read(unknown[7], 0, 4);
			Speed = new CharacterAttribute(reader);
			reader.Read(unknown[8], 0, 4);
			Stamina = new CharacterAttribute(reader);
			reader.Read(unknown[9], 0, 4);
			Luck = new CharacterAttribute(reader);
			reader.Read(unknown[10], 0, 4);
			MagicResistance = new CharacterAttribute(reader);
			reader.Read(unknown[11], 0, 4);
			MagicTalent = new CharacterAttribute(reader);
			reader.Read(unknown[12], 0, 4);
			Age = new CharacterAttribute(reader);
			reader.Read(unknown[13], 0, 12);
			CloseRangeCombat = new CharacterAttribute(reader);
			reader.Read(unknown[14], 0, 4);
			LongRangeCombat = new CharacterAttribute(reader);
			reader.Read(unknown[15], 0, 4);
			CriticalHit = new CharacterAttribute(reader);
			reader.Read(unknown[16], 0, 4);
			Lockpicking = new CharacterAttribute(reader);
			reader.Read(unknown[17], 0, 52);
			LifePoints = new CharacterAttribute(reader);
			reader.Read(unknown[18], 0, 2);
			SpellPoints = new CharacterAttribute(reader);
			reader.Read(unknown[21], 0, 2);
			BaseDefense = reader.ReadUInt16();
			BonusDefense = reader.ReadInt16();
			BaseAttack = reader.ReadUInt16();
			BonusAttack = reader.ReadInt16();
			reader.Read(unknown[19], 0, 4);
			LevelsPerActionPoint = reader.ReadUInt16();
			LifePointsPerLevel = reader.ReadUInt16();
			SpellPointsPerLevel = reader.ReadUInt16();
			reader.Read(unknown[22], 0, 2);
			TrainingPointsPerLevel = reader.ReadUInt16();
			reader.Read(unknown[23], 0, 2);
			Experience = reader.ReadInt32();
			for(int i = 0; i < 7; i++)
			{
				Spells[i] = reader.ReadInt32();
			}
			reader.Read(unknown[20], 0, 4);
			Name = new LanguageTerm(reader);
			originalName = Name;
			for(int i = 0; i < 7; i++)
			{
				for(int j = 0; j < 30; j++)
				{
					SpellStrengths[i,j] = reader.ReadInt16();
				}
			}
		}

		/// <summary>
		/// PORT: implemented (was commented out upstream).
		/// Saves the NPC part of the record (first <see cref="NPCLayout.Size"/> bytes).
		/// When the NPC was loaded from a byte array, the original bytes are used as a base
		/// and only known fields are patched in place, so an unedited record is byte-identical.
		/// </summary>
		public virtual byte[] ToRawData()
		{
			byte[] data;
			if(source != null)
			{
				data = (byte[])source.Clone();
			}else{
				data = new byte[RecordSize];
				WriteUnknown(data);
			}
			WriteFields(data);
			return data;
		}

		/// <summary>
		/// Record size produced by <see cref="ToRawData"/> when no source record exists.
		/// </summary>
		protected virtual int RecordSize{
			get{ return NPCLayout.Size; }
		}

		private void WriteUnknown(byte[] d)
		{
			unknown[1].CopyTo(d, 6);
			unknown[2].CopyTo(d, 12);
			unknown[3].CopyTo(d, 28);
			unknown[4].CopyTo(d, 32);
			unknown[5].CopyTo(d, 46);
			unknown[6].CopyTo(d, 54);
			unknown[7].CopyTo(d, 62);
			unknown[8].CopyTo(d, 70);
			unknown[9].CopyTo(d, 78);
			unknown[10].CopyTo(d, 86);
			unknown[11].CopyTo(d, 94);
			unknown[12].CopyTo(d, 102);
			unknown[13].CopyTo(d, 110);
			unknown[14].CopyTo(d, 126);
			unknown[15].CopyTo(d, 134);
			unknown[16].CopyTo(d, 142);
			unknown[17].CopyTo(d, 150);
			unknown[18].CopyTo(d, 206);
			unknown[19].CopyTo(d, 222);
			unknown[21].CopyTo(d, 212);
			unknown[22].CopyTo(d, 232);
			unknown[23].CopyTo(d, 236);
			unknown[20].CopyTo(d, 270);
		}

		private static void PutInt16(byte[] d, int offset, short value)
		{
			d[offset] = (byte)value;
			d[offset+1] = (byte)(value >> 8);
		}

		private static void PutInt32(byte[] d, int offset, int value)
		{
			d[offset] = (byte)value;
			d[offset+1] = (byte)(value >> 8);
			d[offset+2] = (byte)(value >> 16);
			d[offset+3] = (byte)(value >> 24);
		}

		private static void PutAttribute(byte[] d, int offset, CharacterAttribute attr)
		{
			PutInt16(d, offset, attr.Value);
			PutInt16(d, offset+2, attr.MaximumValue);
		}

		private void WriteFields(byte[] d)
		{
			d[NPCLayout.Type] = (byte)Type;
			d[NPCLayout.Gender] = (byte)Gender;
			d[NPCLayout.Race] = (byte)Race;
			d[NPCLayout.Class] = (byte)Class;
			d[NPCLayout.Magic] = (byte)Magic;
			d[NPCLayout.Level] = Level;
			d[NPCLayout.Language] = (byte)Language;
			d[NPCLayout.Appearance] = Appearance;
			d[NPCLayout.Face] = Face;
			d[NPCLayout.InventoryPicture] = InventoryPicture;
			d[NPCLayout.ActionPoints] = ActionPoints;
			d[NPCLayout.Morale] = Morale;
			PutInt16(d, NPCLayout.EventSetId, unchecked((short)EventSetId));
			PutInt16(d, NPCLayout.WordSetId, unchecked((short)WordSetId));
			PutInt16(d, NPCLayout.TrainingPoints, TrainingPoints);
			PutInt16(d, NPCLayout.Gold, unchecked((short)checked((ushort)decimal.Round(Gold*10M))));
			PutInt16(d, NPCLayout.Rations, Rations);
			PutInt16(d, NPCLayout.Conditions, unchecked((short)Conditions));
			PutAttribute(d, NPCLayout.Strength, Strength);
			PutAttribute(d, NPCLayout.Intelligence, Intelligence);
			PutAttribute(d, NPCLayout.Dexterity, Dexterity);
			PutAttribute(d, NPCLayout.Speed, Speed);
			PutAttribute(d, NPCLayout.Stamina, Stamina);
			PutAttribute(d, NPCLayout.Luck, Luck);
			PutAttribute(d, NPCLayout.MagicResistance, MagicResistance);
			PutAttribute(d, NPCLayout.MagicTalent, MagicTalent);
			PutAttribute(d, NPCLayout.Age, Age);
			PutAttribute(d, NPCLayout.CloseRangeCombat, CloseRangeCombat);
			PutAttribute(d, NPCLayout.LongRangeCombat, LongRangeCombat);
			PutAttribute(d, NPCLayout.CriticalHit, CriticalHit);
			PutAttribute(d, NPCLayout.Lockpicking, Lockpicking);
			PutAttribute(d, NPCLayout.LifePoints, LifePoints);
			PutAttribute(d, NPCLayout.SpellPoints, SpellPoints);
			PutInt16(d, NPCLayout.BaseDefense, unchecked((short)BaseDefense));
			PutInt16(d, NPCLayout.BonusDefense, BonusDefense);
			PutInt16(d, NPCLayout.BaseAttack, unchecked((short)BaseAttack));
			PutInt16(d, NPCLayout.BonusAttack, BonusAttack);
			PutInt16(d, NPCLayout.LevelsPerActionPoint, unchecked((short)LevelsPerActionPoint));
			PutInt16(d, NPCLayout.LifePointsPerLevel, unchecked((short)LifePointsPerLevel));
			PutInt16(d, NPCLayout.SpellPointsPerLevel, unchecked((short)SpellPointsPerLevel));
			PutInt16(d, NPCLayout.TrainingPointsPerLevel, unchecked((short)TrainingPointsPerLevel));
			// Upstream's commented-out writer used 239 here, but the reader (and the
			// spell block that follows at 242) place Experience at 238.
			PutInt32(d, NPCLayout.Experience, Experience);
			for(int i = 0; i < 7; i++)
			{
				PutInt32(d, NPCLayout.Spells+i*4, Spells[i]);
			}
			WriteName(d);
			for(int i = 0; i < 7; i++)
			{
				for(int j = 0; j < 30; j++)
				{
					PutInt16(d, NPCLayout.SpellStrengths+(i*30+j)*2, SpellStrengths[i,j]);
				}
			}
		}

		private void WriteName(byte[] d)
		{
			bool fresh = source == null;
			WriteNameSlot(d, NPCLayout.Name, Name.German, fresh || Name.German != originalName.German);
			WriteNameSlot(d, NPCLayout.Name+NPCLayout.NameSlotSize, Name.English, fresh || Name.English != originalName.English);
			WriteNameSlot(d, NPCLayout.Name+2*NPCLayout.NameSlotSize, Name.French, fresh || Name.French != originalName.French);
		}

		private static void WriteNameSlot(byte[] d, int offset, string value, bool changed)
		{
			// Unchanged names are left untouched: bytes after the NUL terminator may hold
			// garbage that has to be preserved for a byte-identical round trip.
			if(!changed) return;
			byte[] bytes = TextCore.DefaultEncoding.GetBytes(value ?? "");
			if(bytes.Length > NPCLayout.NameSlotSize)
				throw new ArgumentException("Name is longer than "+NPCLayout.NameSlotSize+" bytes.");
			Array.Clear(d, offset, NPCLayout.NameSlotSize);
			bytes.CopyTo(d, offset);
		}

		int IGameResource.Save(Stream output)
		{
			throw new NotImplementedException();
		}

		public bool Equals(IGameResource other)
		{
			return Equals((object)other);
		}

		public override bool Equals(object obj)
		{
			NPC other = obj as NPC;
			if (other == null)
				return false;
			return object.Equals(this.unknown, other.unknown) && this.Type == other.Type && this.Gender == other.Gender && this.Race == other.Race && this.Class == other.Class && this.Magic == other.Magic && this.Level == other.Level && this.Language == other.Language && this.Appearance == other.Appearance && this.Face == other.Face && this.InventoryPicture == other.InventoryPicture && this.ActionPoints == other.ActionPoints && this.EventSetId == other.EventSetId && this.WordSetId == other.WordSetId && this.Morale == other.Morale && this.TrainingPoints == other.TrainingPoints && object.Equals(this.Gold, other.Gold) && this.Rations == other.Rations && this.Conditions == other.Conditions && this.Strength == other.Strength && this.Intelligence == other.Intelligence && this.Dexterity == other.Dexterity && this.Speed == other.Speed && this.Stamina == other.Stamina && this.Luck == other.Luck && this.MagicResistance == other.MagicResistance && this.MagicTalent == other.MagicTalent && this.CloseRangeCombat == other.CloseRangeCombat && this.LongRangeCombat == other.LongRangeCombat && this.CriticalHit == other.CriticalHit && this.Lockpicking == other.Lockpicking && this.LifePoints == other.LifePoints && this.SpellPoints == other.SpellPoints && this.Age == other.Age && this.BaseDefense == other.BaseDefense && this.BonusDefense == other.BonusDefense && this.BaseAttack == other.BaseAttack && this.BonusAttack == other.BonusAttack && this.Experience == other.Experience && object.Equals(this.Spells, other.Spells) && object.Equals(this.Name, other.Name) && object.Equals(this.SpellStrengths, other.SpellStrengths);
		}

		public override int GetHashCode()
		{
			int hashCode = 0;
			unchecked {
				if (unknown != null)
					hashCode += 1000000007 * unknown.GetHashCode();
				hashCode += 1000000009 * Type.GetHashCode();
				hashCode += 1000000021 * Gender.GetHashCode();
				hashCode += 1000000033 * Race.GetHashCode();
				hashCode += 1000000087 * Class.GetHashCode();
				hashCode += 1000000093 * Magic.GetHashCode();
				hashCode += 1000000097 * Level.GetHashCode();
				hashCode += 1000000103 * Language.GetHashCode();
				hashCode += 1000000123 * Appearance.GetHashCode();
				hashCode += 1000000181 * Face.GetHashCode();
				hashCode += 1000000207 * InventoryPicture.GetHashCode();
				hashCode += 1000000223 * ActionPoints.GetHashCode();
				hashCode += 1000000241 * EventSetId.GetHashCode();
				hashCode += 1000000271 * WordSetId.GetHashCode();
				hashCode += 1000000289 * TrainingPoints.GetHashCode();
				hashCode += 1000000297 * Gold.GetHashCode();
				hashCode += 1000000321 * Rations.GetHashCode();
				hashCode += 1000000349 * Conditions.GetHashCode();
				hashCode += 1000000363 * Strength.GetHashCode();
				hashCode += 1000000403 * Intelligence.GetHashCode();
				hashCode += 1000000409 * Dexterity.GetHashCode();
				hashCode += 1000000411 * Speed.GetHashCode();
				hashCode += 1000000427 * Stamina.GetHashCode();
				hashCode += 1000000433 * Luck.GetHashCode();
				hashCode += 1000000439 * MagicResistance.GetHashCode();
				hashCode += 1000000447 * MagicTalent.GetHashCode();
				hashCode += 1000000453 * CloseRangeCombat.GetHashCode();
				hashCode += 1000000459 * LongRangeCombat.GetHashCode();
				hashCode += 1000000483 * CriticalHit.GetHashCode();
				hashCode += 1000000513 * Lockpicking.GetHashCode();
				hashCode += 1000000531 * LifePoints.GetHashCode();
				hashCode += 1000000579 * SpellPoints.GetHashCode();
				hashCode += 1000000007 * Age.GetHashCode();
				hashCode += 1000000009 * BaseDefense.GetHashCode();
				hashCode += 1000000021 * BonusDefense.GetHashCode();
				hashCode += 1000000033 * BaseAttack.GetHashCode();
				hashCode += 1000000087 * BonusAttack.GetHashCode();
				hashCode += 1000000093 * Experience.GetHashCode();
				if (Spells != null)
					hashCode += 1000000097 * Spells.GetHashCode();
				if (Name != null)
					hashCode += 1000000103 * Name.GetHashCode();
				if (SpellStrengths != null)
					hashCode += 1000000123 * SpellStrengths.GetHashCode();
			}
			return hashCode;
		}

		public static bool operator ==(NPC lhs, NPC rhs)
		{
			if (ReferenceEquals(lhs, rhs))
				return true;
			if (ReferenceEquals(lhs, null) || ReferenceEquals(rhs, null))
				return false;
			return lhs.Equals(rhs);
		}

		public static bool operator !=(NPC lhs, NPC rhs)
		{
			return !(lhs == rhs);
		}

	}
}