/* PORT: new. Item definition flags (ITEMLIST.DAT offset 27, uint16); names follow UAlbion's ItemFlags.
   Stackable verified on the GOG data (set on all 225 stackable items, never on gear). */
using System;

namespace AlbLib.Items
{
	[Flags, Serializable]
	public enum ItemTypeFlags : ushort
	{
		None = 0,
		/// <summary>Quest item.</summary>
		PlotItem = 0x0002,
		/// <summary>Several items can share one slot (max 99, hard-coded in the game).</summary>
		Stackable = 0x0004,
		/// <summary>Item is cursed.</summary>
		Cursed = 0x0400
	}
}
