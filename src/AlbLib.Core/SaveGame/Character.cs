using System;
using System.IO;
using System.Text;
using AlbLib.Texts;

namespace AlbLib.SaveGame
{
	/// <summary>
	/// Stores information about game character.
	/// </summary>
	[Serializable]
	public class Character : NPC
	{
		/// <summary>
		/// Character equipment items.
		/// </summary>
		public Equipment Equipment{get; private set;}
		
		/// <summary>
		/// Character backpack items.
		/// </summary>
		public Inventory Backpack{get; private set;}
		
		/// <summary>
		/// Creates new character.
		/// </summary>
		public Character() : base()
		{
			Equipment = new Equipment();
			Backpack = new Inventory(24);
		}
		
		/// <summary>
		/// Loads character.
		/// </summary>
		/// <param name="data">
		/// Byte array containing character data.
		/// </param>
		public Character(byte[] data) : base(data)
		{
			Equipment = new Equipment(data, 742);
			Backpack = new Inventory(data, 796, 24);
		}
		
		public Character(Stream stream) : this(new BinaryReader(stream, TextCore.DefaultEncoding))
		{}
		
		public Character(BinaryReader reader) : base(reader)
		{
			Equipment = new Equipment(reader);
			Backpack = new Inventory(reader, 24);
		}
		
		/// <summary>
		/// PORT: implemented (was commented out upstream).
		/// Saves the whole character record; unknown bytes are preserved from the loaded record.
		/// </summary>
		public override byte[] ToRawData()
		{
			byte[] data = base.ToRawData();
			Equipment.ToRawData().CopyTo(data, NPCLayout.Equipment);
			Backpack.ToRawData().CopyTo(data, NPCLayout.Backpack);
			return data;
		}

		protected override int RecordSize{
			get{ return NPCLayout.CharacterSize; }
		}
	}
}