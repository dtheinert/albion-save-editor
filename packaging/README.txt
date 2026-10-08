Albion Save Editor
==================

A small Windows tool to view and edit savegames of the 1995 DOS RPG "Albion":
party stats, attributes, skills, spells, gold, rations, equipment and backpack items.

Requirements
------------
- Windows 10 or 11 (64-bit). Nothing else to install: the program is a single .exe.
- The editor window uses Microsoft Edge WebView2, which is part of current Windows.
  If it is missing, the editor opens in your normal web browser instead and a small
  window "Editor running - close this to quit" stays open.

How to use
----------
1. BACK UP YOUR SAVES FIRST. Copy the whole SAVES folder somewhere safe.
2. Start AlbionSaveEditor.exe (you can put it anywhere; it does not need to be in the
   Albion folder).
   The first time, Windows may show "Windows protected your PC" (SmartScreen), because
   the program is not code-signed. Click "More info" and then "Run anyway".
3. Click "Choose save file..." (or drag a save onto the window) and pick a save file
   (SAVE.001, SAVE.002, ...).
4. Edit what you like. Values are checked against the game's limits.
5. Click "Download modified save" and save the file. Choose the original file name
   (e.g. SAVE.001) and put it into Albion's SAVES folder, replacing the old one
   (you made a backup, right?).
6. Start Albion and load the save.

The editor never writes into your Albion folder by itself: it only saves where you tell it to.

Where are the saves?
--------------------
- GOG Galaxy:  C:\Program Files (x86)\GOG Galaxy\Games\Albion\SAVES
               (GOG cloud saves: ...\Albion\cloud_saves\SAVES)
- GOG offline: C:\GOG Games\Albion\SAVES
- Steam:       ...\steamapps\common\Albion\SAVES
- CD / DOSBox: ALBION\SAVES in your Albion folder.
The files are named SAVE.001 ... SAVE.010 (slots in the game's load menu).

Item and spell names
--------------------
Names are read from your own Albion installation (no game files are included).
GOG and Steam installations are found automatically; the GOG version stores the data in
game.gog, which the editor reads directly. Otherwise use the editor's game data setting and
point it to your Albion folder (the one containing XLDLIBS) or to the CD image.

Troubleshooting
---------------
A log file is written to %LOCALAPPDATA%\AlbionSaveEditor\editor.log.
Browser data of the editor window is kept in %LOCALAPPDATA%\AlbionSaveEditor\WebView2
(you can delete that folder at any time).

Credits
-------
- AlbLib by IS4Code (IllidanS4), https://github.com/IS4Code/AlbLib, MIT license
  (see LICENSE-AlbLib.txt). The save, XLD and text parsing is a port of AlbLib with
  fixes and additions.
- UAlbion by csinkers and contributors, https://github.com/csinkers/ualbion:
  file format research (character sheet layout, item flags, stack limits).
  No UAlbion code is included.
- Albion is (c) Blue Byte. This is an unofficial fan tool.
