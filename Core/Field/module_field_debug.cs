﻿using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using OpenVIII.Encoding.Tags;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace OpenVIII
{
    //issues found.
    //558 //color looks off on glow. with purple around it.
    //267 //text showing with white background.
    //132 missing lava.
    //pupu states that there is are 2 widths of the texture for Type1 and Type2
    //we are only using 1 so might be reading the wrong pixels somewhere.
    public static partial class Module_field_debug
    {
        public enum BlendMode : byte
        {
            halfadd,
            add,
            subtract,
            quarteradd,
            none,
        }

        private static Field_mods mod = 0;

        //private static Texture2D tex;
        //private static Texture2D texOverlap;
        private static ConcurrentDictionary<ushort, ConcurrentDictionary<byte, ConcurrentDictionary<byte, ConcurrentDictionary<byte, ConcurrentDictionary<byte, ConcurrentDictionary<BlendMode, Texture2D>>>>>> Textures;

        private static EventEngine eventEngine;
        private static IServices services;
        private static List<Tile> tiles;

        private static int Width, Height;

        private enum Field_mods
        {
            INIT,
            DEBUGRENDER,
            DISABLED
        };

        public static void Draw()
        {
            switch (mod)
            {
                case Field_mods.INIT:
                    break; //null
                case Field_mods.DEBUGRENDER:
                    DrawDebug();
                    break;
            }
        }

        public static void ResetField() => mod = Field_mods.INIT;

        private static List<KeyValuePair<BlendMode, Texture2D>> drawtextures() =>
            Textures?.OrderByDescending(kvp_Z => kvp_Z.Key)
            .SelectMany(kvp_LayerID => kvp_LayerID.Value.OrderBy(x => kvp_LayerID.Key)
            .SelectMany(kvp_AnimationID => kvp_AnimationID.Value.OrderBy(x => kvp_AnimationID.Key))
            .SelectMany(kvp_AnimationState => kvp_AnimationState.Value.OrderBy(x => kvp_AnimationState.Key))
            .SelectMany(kvp_OverlapID => kvp_OverlapID.Value.OrderBy(x => kvp_OverlapID.Key))
            .SelectMany(kvp_BlendMode => kvp_BlendMode.Value)).ToList();

        private static void DrawDebug()
        {
            Memory.graphics.GraphicsDevice.Clear(Color.Black);

            List<KeyValuePair<BlendMode, Texture2D>> _drawtextures = drawtextures();
            bool open = false;
            BlendMode lastbm = BlendMode.none;
            float alpha = 1f;
            if (_drawtextures != null)
                foreach (KeyValuePair<BlendMode, Texture2D> kvp in _drawtextures)
                {
                    if (!open || lastbm != kvp.Key)
                    {
                        if (open)
                            Memory.SpriteBatchEnd();
                        open = true;
                        alpha = 1f;
                        switch (kvp.Key)
                        {
                            default:
                                Memory.SpriteBatchStartAlpha();
                                break;

                            case BlendMode.halfadd:
                                Memory.SpriteBatchStart(bs: Memory.blendState_Add);
                                break;

                            case BlendMode.quarteradd:
                                Memory.SpriteBatchStart(bs: Memory.blendState_Add);
                                break;

                            case BlendMode.add:
                                Memory.SpriteBatchStart(bs: Memory.blendState_Add);
                                break;

                            case BlendMode.subtract:
                                alpha = .9f;
                                Memory.SpriteBatchStart(bs: Memory.blendState_Subtract);
                                break;
                        }
                        lastbm = kvp.Key;
                    }
                    Texture2D tex = kvp.Value;
                    Rectangle src = new Rectangle(0, 0, tex.Width, tex.Height);
                    Rectangle dst = src;
                    dst.Size = (dst.Size.ToVector2() * Memory.Scale(tex.Width, tex.Height, Memory.ScaleMode.FitBoth)).ToPoint();
                    //In game I think we'd keep the field from leaving the screen edge but would center on the Squall and the party when it can.
                    //I setup scaling after noticing the field didn't size with the screen. I set it to center on screen.
                    dst.Offset(Memory.Center.X - dst.Center.X, Memory.Center.Y - dst.Center.Y);
                    Memory.spriteBatch.Draw(tex, dst, src, Color.White * alpha);
                    //new Microsoft.Xna.Framework.Rectangle(0, 0, 1280 + (width - 320), 720 + (height - 224)),
                    //new Microsoft.Xna.Framework.Rectangle(0, 0, tex.Width, tex.Height)
                }
            if (open)
                Memory.SpriteBatchEnd();
            Memory.SpriteBatchStartAlpha();
            Memory.font.RenderBasicText($"FieldID: {Memory.FieldHolder.FieldID} - {Memory.FieldHolder.fields[Memory.FieldHolder.FieldID].ToUpper()}\nHas 4-Bit locations: {Is4Bit}", new Point(20, 20), new Vector2(3f));
            Memory.SpriteBatchEnd();
        }

        public static void Update()
        {
#if DEBUG
            // lets you move through all the feilds just holding left or right. it will just loop when it runs out.
            if (Input2.DelayedButton(FF8TextTagKey.Left))
            {
                init_debugger_Audio.PlaySound(0);
                if (Memory.FieldHolder.FieldID > 0)
                    Memory.FieldHolder.FieldID--;
                else
                    Memory.FieldHolder.FieldID = checked((ushort)(Memory.FieldHolder.fields.Length - 1));
                ResetField();
            }
            if (Input2.DelayedButton(FF8TextTagKey.Right))
            {
                init_debugger_Audio.PlaySound(0);
                if (Memory.FieldHolder.FieldID < checked((ushort)(Memory.FieldHolder.fields.Length - 1)))
                    Memory.FieldHolder.FieldID++;
                else
                    Memory.FieldHolder.FieldID = 0;
                ResetField();
            }
#endif
            switch (mod)
            {
                case Field_mods.INIT:
                    Init();
                    break;

                case Field_mods.DEBUGRENDER:
                    UpdateScript();
                    break; //await events here
                case Field_mods.DISABLED:
                    break;
            }
        }

        private static void UpdateScript()
        {
            //We do not know every instruction and it's not possible for now to play field with unknown instruction
            //eventEngine.Update(services);
        }

        private static void Init()
        {
            ArchiveWorker aw = new ArchiveWorker(Memory.Archives.A_FIELD);
            string[] test = aw.GetListOfFiles();

            //TODO fix endless look on FieldID 50.
            if (Memory.FieldHolder.FieldID >= Memory.FieldHolder.fields.Length ||
                Memory.FieldHolder.FieldID < 0)
                return;
            string fieldArchiveName = test.FirstOrDefault(x => x.IndexOf(Memory.FieldHolder.fields[Memory.FieldHolder.FieldID], StringComparison.OrdinalIgnoreCase) >= 0);
            if (string.IsNullOrWhiteSpace(fieldArchiveName)) return;

            ArchiveWorker fieldArchive = aw.GetArchive(fieldArchiveName);
            string[] filelist = fieldArchive.GetListOfFiles();
            string findstr(string s) =>
                filelist.FirstOrDefault(x => x.IndexOf(s, StringComparison.OrdinalIgnoreCase) >= 0);

            byte[] getfile(string s)
            {
                s = findstr(s);
                if (!string.IsNullOrWhiteSpace(s))
                    return fieldArchive.GetBinaryFile(s);
                else
                    return null;
            }
            string s_jsm = findstr(".jsm");
            string s_sy = findstr(".sy");
            if (!ParseBackgroundQuads(getfile(".mim"), getfile(".map")))
                return;
            //let's start with scripts
            List<Jsm.GameObject> jsmObjects;

            if (!string.IsNullOrWhiteSpace(s_jsm))
            {
                try
                {
                    jsmObjects = Jsm.File.Read(fieldArchive.GetBinaryFile(s_jsm));
                }
                catch (Exception e)
                {
                    Debug.WriteLine(e);
                    mod = Field_mods.DISABLED;
                    return;
                }

                Sym.GameObjects symObjects;
                if (!string.IsNullOrWhiteSpace(s_sy))
                {
                    symObjects = Sym.Reader.FromBytes(fieldArchive.GetBinaryFile(s_sy));
                }
                else
                    return;
                services = FieldInitializer.GetServices();
                eventEngine = ServiceId.Field[services].Engine;
                eventEngine.Reset();
                for (int objIndex = 0; objIndex < jsmObjects.Count; objIndex++)
                {
                    Jsm.GameObject obj = jsmObjects[objIndex];
                    FieldObject fieldObject = new FieldObject(obj.Id, symObjects.GetObjectOrDefault(objIndex).Name);

                    foreach (Jsm.GameScript script in obj.Scripts)
                        fieldObject.Scripts.Add(script.ScriptId, script.Segment.GetExecuter());

                    eventEngine.RegisterObject(fieldObject);
                }
            }
            else
            {
                mod = Field_mods.DISABLED;
                return;
            }

            //byte[] mchb = getfile(".mch");//Field character models
            //byte[] oneb = getfile(".one");//Field character models container
            //byte[] msdb = getfile(".msd");//dialogs
            //byte[] infb = getfile(".inf");//gateways
            //byte[] idb = getfile(".id");//walkmesh
            //byte[] cab = getfile(".ca");//camera
            //byte[] tdwb = getfile(".tdw");//extra font
            //byte[] mskb = getfile(".msk");//movie cam
            //byte[] ratb = getfile(".rat");//battle on field
            //byte[] pmdb = getfile(".pmd");//particle info
            //byte[] sfxb = getfile(".sfx");//sound effects

            mod++;
            return;
        }

        public class TextureBuffer
        {
            private Color[] colors;
            public bool Alert { get; set; }

            public TextureBuffer(int width, int height, bool alert = true)
            {
                Height = height;
                Width = width;
                colors = new Color[height * width];
                Alert = alert;
            }

            public int Height { get; private set; }
            public int Width { get; private set; }
            public Color[] Colors { get => colors; private set => colors = value; }
            public int Length => colors?.Length ?? 0;

            public Color this[int i]
            {
                get => colors[i]; set
                {
                    if (Alert && colors[i] != Color.TransparentBlack)
                        throw new Exception("Color is set!");
                    colors[i] = value;
                }
            }

            public Color this[int x, int y]
            {
                get => colors[x + (y * Width)]; set => this[x + (y * Width)] = value;
            }

            public static implicit operator Color[] (TextureBuffer @in) => @in.colors;

            public static explicit operator Texture2D(TextureBuffer @in)
            {
                Texture2D tex = new Texture2D(Memory.graphics.GraphicsDevice, @in.Width, @in.Height);
                @in.SetData(tex);
                return tex;
            }

            public Color[] this[Rectangle rectangle]
            {
                get => this[rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height];
                set => this[rectangle.X, rectangle.Y, rectangle.Width, rectangle.Height] = value;
            }

            public void SetData(Texture2D tex) => tex.SetData(colors);

            public Color[] this[int x, int y, int width, int height]
            {
                get
                {
                    int pos = (x + y * Width);
                    List<Color> r = new List<Color>(width * height);
                    int row = 0;
                    while (r.Count < width * height)
                    {
                        r.AddRange(colors.Skip(pos + row * Width).Take(width));
                        row++;
                    }
                    return r.ToArray();
                }
                set
                {
                    for (int _y = y; (_y - y) < height; _y++)
                        for (int _x = x; (_x - x) < width; _x++)
                        {
                            int pos = (_x + _y * Width);
                            colors[pos] = value[(_x - x) + (_y - y) * width];
                        }
                }
            }
        }

        private static TextureType[] TextureTypes = new TextureType[]
        {
            new TextureType {
                Palettes =24,
                TexturePages = 13
            },
            new TextureType {
                Palettes =16,
                TexturePages = 12,
                SkippedPalettes =0,
                Type = 2
            },
        };

        private static bool Is4Bit = false;

        private static bool ParseBackgroundQuads(byte[] mimb, byte[] mapb)
        {
            Is4Bit = false;
            if (mimb == null || mapb == null)
                return false;
            TextureType textureType = TextureTypes.FirstOrDefault(x => x.FileSize == mimb.Length);
            if (textureType == default)
            {//unsupported feild dump data so can check it.
                string path = Path.Combine(Path.GetTempPath(), "Fields", $"{Memory.FieldHolder.fields[Memory.FieldHolder.FieldID]}");
                using (BinaryWriter bw = new BinaryWriter(new FileStream($"{path}.mim", FileMode.Create, FileAccess.Write, FileShare.ReadWrite)))
                {
                    bw.Write(mimb);
                    Debug.WriteLine($"Saved {path}.mim");
                }
                using (BinaryWriter bw = new BinaryWriter(new FileStream($"{path}.map", FileMode.Create, FileAccess.Write, FileShare.ReadWrite)))
                {
                    bw.Write(mapb);
                    Debug.WriteLine($"Saved {path}.map");
                }
                return false;
            }
            tiles = GetTiles(mapb, textureType);
            FindOverlappingTiles();
            FindOverlappingTilesSource();
            Dictionary<byte, Color[]> dictPalettes = GetPalettes(mimb, textureType);


            List<Tile> sortedtiles = (from tile in tiles
                                      orderby tile.TextureID, tile.Is4Bit, tile.TileID ascending
                                      select tile
                                ).ToList();
            // Create a swizzeled Textures with one palette.
            // 4bit has 2 pixels per byte. So will need a seperate texture for those.
            ushort X = sortedtiles.Min(x => x.SourceX);
            Width = sortedtiles.Max(x => x.SourceX + Tile.size);
            ushort Y = sortedtiles.Min(x => x.SourceX);
            Height = sortedtiles.Max(x => x.SourceY + Tile.size);
            Dictionary<byte, Texture2D> TextureIDs = sortedtiles.Select(x => x.TextureID).Distinct().ToDictionary(x => x, x => new Texture2D(Memory.graphics.GraphicsDevice, 256, 256/*Math.Max(Width,Height), Height*/));
            using (BinaryReader br = new BinaryReader(new MemoryStream(mimb)))
                foreach (KeyValuePair<byte, Texture2D> kvp in TextureIDs)
                {
                    TextureBuffer tex = new TextureBuffer(kvp.Value.Width, kvp.Value.Height, true);

                    foreach (Tile tile in sortedtiles.Where(x => x.TextureID == kvp.Key))
                    {
                        if (tile.Skip) continue;
                        long startPixel = textureType.PaletteSectionSize + (tile.SourceX / (tile.Is4Bit ? 2 : 1)) + (texturewidth * tile.TextureID) + (textureType.Width * tile.SourceY);
                        int readlength = Tile.size + (Tile.size * textureType.Width);
                        for (int y = 0; y < 16; y++)
                        {
                            br.BaseStream.Seek(startPixel + (y * textureType.Width), SeekOrigin.Begin);

                            byte Colorkey = 0;
                            int _y = y + tile.SourceY;
                            for (int x = 0; x < 16; x++)
                            {
                                int _x = x + tile.SourceX;
                                if (tile.Is8Bit)
                                {
                                    tex[_x, _y] = dictPalettes[tile.PaletteID][br.ReadByte()];
                                }
                                else
                                {
                                    if (x % 2 == 0)
                                    {
                                        Colorkey = br.ReadByte();
                                        tex[_x, _y] = dictPalettes[tile.PaletteID][Colorkey & 0xf];
                                    }
                                    else
                                    {
                                        tex[_x, _y] = dictPalettes[tile.PaletteID][(Colorkey & 0xf0) >> 4];
                                    }
                                }
                            }
                        }
                        //(from b in br.ReadBytes(readlength)
                        // select dictPalettes[tile.PaletteID][b]).ToArray();
                    }
                    tex.SetData(kvp.Value);
                }
            SaveSwizzled(TextureIDs);
            TextureIDs.ForEach(x => x.Value.Dispose());
            return true;
        }

        private static void SaveSwizzled(Dictionary<byte, Texture2D> TextureIDs)
        {
            foreach (KeyValuePair<byte, Texture2D> kvp in TextureIDs)
            {
                string fieldname = Memory.FieldHolder.fields[Memory.FieldHolder.FieldID].ToLower();
                if (string.IsNullOrWhiteSpace(fieldname))
                    fieldname = $"unk{Memory.FieldHolder.FieldID}";
                string folder = Path.Combine(Path.GetTempPath(), "Fields", fieldname.Substring(0, 2), fieldname);
                Directory.CreateDirectory(folder);
                string path = Path.Combine(folder,
                    $"{fieldname}_{kvp.Key}.png");
                if (File.Exists(path))
                    continue;
                using (FileStream fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
                { kvp.Value.SaveAsPng(fs, kvp.Value.Width, kvp.Value.Height); }
            }
        }

        private const int texturewidth = 128;
        private const int colorsPerPalette = 256;
        private const int bytesPerPalette = 2 * colorsPerPalette; //16 bit color (2 bytes) * 256 colors

        //private static int palettes = 24; // or 16;

        private class TextureType
        {
            public byte Type = 1;
            public byte Palettes;
            public ushort Width => checked((ushort)(TWidth * TexturePages));
            public byte TexturePages;
            public byte SkippedPalettes = 8;

            private const ushort TWidth = 128;
            private const ushort THeight = 256;
            public uint PaletteSectionSize => checked((uint)(bytesPerPalette * Palettes));
            public uint FileSize => checked((uint)(PaletteSectionSize + (Width * THeight)));

            public int BytesSkippedPalettes => bytesPerPalette * SkippedPalettes; // first 8 are junk and are not used.
        }

        private static bool ParseBackground2D(byte[] mimb, byte[] mapb)
        {
            Is4Bit = false;
            if (mimb == null || mapb == null)
                return false;

            TextureType textureType = TextureTypes.First(x => x.FileSize == mimb.Length);
            tiles = GetTiles(mapb,textureType);
            FindOverlappingTiles();
            FindOverlappingTilesSource();
            int lowestY = tiles.Min(x => x.Y);
            int maximumY = tiles.Max(x => x.Y);
            int lowestX = tiles.Min(x => x.X); //-160;
            int maximumX = tiles.Max(x => x.X);
            Dictionary<byte, Color[]> dictPalettes = GetPalettes(mimb, textureType);
            Height = Math.Abs(lowestY) + maximumY + Tile.size; //224
            Width = Math.Abs(lowestX) + maximumX + Tile.size; //320
                                                              //Color[] finalImage = new Color[height * width]; //ARGB;
                                                              //Color[] finalOverlapImage = new Color[height * width];
                                                              //tex = new Texture2D(Memory.graphics.GraphicsDevice, width, height);
                                                              //texOverlap = new Texture2D(Memory.graphics.GraphicsDevice, width, height);
            IOrderedEnumerable<byte> layers = tiles.Select(x => x.LayerID).Distinct().OrderBy(x => x);
            Debug.WriteLine($"FieldID: {Memory.FieldHolder.FieldID}, Layers: {layers.Count()}, ({string.Join(",", layers.ToArray())}) ");
            byte MaximumLayer = layers.Max();
            byte MinimumLayer = layers.Min();
            IOrderedEnumerable<ushort> BufferDepth = tiles.Select(x => x.Z).Distinct().OrderByDescending(x => x); // larger number is farther away.

            List<Tile> sortedtiles = tiles.OrderBy(x => x.OverLapID).ThenByDescending(x => x.Z).ThenBy(x => x.LayerID).ThenBy(x => x.AnimationID).ThenBy(x => x.AnimationState).ThenBy(x => x.BlendMode).ToList();

            if (Textures != null)
            {
                foreach (Texture2D tex in drawtextures().Select(x => x.Value))
                    tex.Dispose();
            }
            Textures = new ConcurrentDictionary<ushort, ConcurrentDictionary<byte, ConcurrentDictionary<byte, ConcurrentDictionary<byte, ConcurrentDictionary<byte, ConcurrentDictionary<BlendMode, Texture2D>>>>>>();
            ushort z = 0;
            byte layerID = 0;
            byte animationID = 0;
            byte animationState = 0;
            byte overlapID = 0;
            BlendMode blendmode = BlendMode.none;
            TextureBuffer texturebuffer = null;
            bool hasColor = false;
            void convertColorToTexture2d()
            {
                if (!hasColor || texturebuffer == null) return;
                hasColor = false;
                ConcurrentDictionary<byte, ConcurrentDictionary<byte, ConcurrentDictionary<byte, ConcurrentDictionary<byte, ConcurrentDictionary<BlendMode, Texture2D>>>>> dictLayerID = Textures.GetOrAdd(z, new ConcurrentDictionary<byte, ConcurrentDictionary<byte, ConcurrentDictionary<byte, ConcurrentDictionary<byte, ConcurrentDictionary<BlendMode, Texture2D>>>>>());
                ConcurrentDictionary<byte, ConcurrentDictionary<byte, ConcurrentDictionary<byte, ConcurrentDictionary<BlendMode, Texture2D>>>> dictAnimationID = dictLayerID.GetOrAdd(layerID, new ConcurrentDictionary<byte, ConcurrentDictionary<byte, ConcurrentDictionary<byte, ConcurrentDictionary<BlendMode, Texture2D>>>>());
                ConcurrentDictionary<byte, ConcurrentDictionary<byte, ConcurrentDictionary<BlendMode, Texture2D>>> dictAnimationState = dictAnimationID.GetOrAdd(animationID, new ConcurrentDictionary<byte, ConcurrentDictionary<byte, ConcurrentDictionary<BlendMode, Texture2D>>>());
                ConcurrentDictionary<byte, ConcurrentDictionary<BlendMode, Texture2D>> dictOverlapID = dictAnimationState.GetOrAdd(animationState, new ConcurrentDictionary<byte, ConcurrentDictionary<BlendMode, Texture2D>>());
                ConcurrentDictionary<BlendMode, Texture2D> dictblend = dictOverlapID.GetOrAdd(overlapID, new ConcurrentDictionary<BlendMode, Texture2D>());
                Texture2D tex = dictblend.GetOrAdd(blendmode, new Texture2D(Memory.graphics.GraphicsDevice, Width, Height));
                texturebuffer.SetData(tex);
            }
            for (int i = 0; i < sortedtiles.Count; i++)
            {
                Tile previousTile = (i > 0) ? previousTile = sortedtiles[i - 1] : null;
                Tile tile = sortedtiles[i];
                if (texturebuffer == null || previousTile == null ||
                    (previousTile.Z != tile.Z ||
                    previousTile.LayerID != tile.LayerID ||
                    previousTile.BlendMode != tile.BlendMode ||
                    previousTile.AnimationID != tile.AnimationID ||
                    previousTile.AnimationState != tile.AnimationState ||
                    previousTile.OverLapID != tile.OverLapID))
                {
                    convertColorToTexture2d();
                    texturebuffer = new TextureBuffer(Width, Height);
                    z = tile.Z;
                    layerID = tile.LayerID;
                    blendmode = tile.BlendMode;
                    animationID = tile.AnimationID;
                    animationState = tile.AnimationState;
                    overlapID = tile.OverLapID;
                }

                int palettePointer = textureType.BytesSkippedPalettes + ((tile.PaletteID) * bytesPerPalette);
                int sourceImagePointer = bytesPerPalette * textureType.Palettes;
                const int texturewidth = 128;
                int startPixel = sourceImagePointer + tile.SourceX + texturewidth * tile.TextureID + (textureType.Width * tile.SourceY);

                int realX = Math.Abs(lowestX) + tile.X; //baseX
                int realY = Math.Abs(lowestY) + tile.Y; //*width
                int realDestinationPixel = ((realY * Width) + realX);

                Rectangle dst = new Rectangle(realX, realY, Tile.size, Tile.size);
                if (tile.Is4Bit)
                {
                    startPixel -= tile.SourceX / 2;
                    Is4Bit = true;
                }
                for (int y = 0; y < Tile.size; y++)
                    for (int x = 0; x < Tile.size; x++)
                    {
                        byte colorKey = GetColorKey(mimb, textureType.Width, startPixel, x, y, tile.Is8Bit);
                        ushort color16bit = BitConverter.ToUInt16(mimb, 2 * colorKey + palettePointer);
                        if (color16bit == 0) // 0 is Color.TransparentBlack So we skip it.
                            continue;
                        Color color = Texture_Base.ABGR1555toRGBA32bit(color16bit);

                        int pos = realDestinationPixel + (x) + (y * Width);
                        Color bufferedcolor = texturebuffer[pos];
                        if (blendmode < BlendMode.none)
                        {
                            if (color == Color.Black)
                                continue;

                            if (blendmode == BlendMode.subtract)
                            {
                                if (bufferedcolor != Color.TransparentBlack)
                                    color = blend2(bufferedcolor, color);
                            }
                            else
                            {
                                if (blendmode == BlendMode.quarteradd)
                                    color = Color.Multiply(color, .25f);
                                else if (blendmode == BlendMode.halfadd)
                                    color = Color.Multiply(color, .5f);
                                if (bufferedcolor != Color.TransparentBlack)
                                    color = blend1(bufferedcolor, color);
                            }
                        }
                        else if (bufferedcolor != Color.TransparentBlack)
                        {
                            throw new Exception("Color is already set something may be wrong.");
                        }
                        color.A = 0xFF;
                        texturebuffer[pos] = color;
                        hasColor = true;
                    }
            }
            convertColorToTexture2d(); // gets leftover colors from last batch and makes a texture.
            SaveTextures();
            return true;
        }

        private static Dictionary<byte, Color[]> GetPalettes(byte[] mimb, TextureType textureType)
        {
            Dictionary<byte, Color[]> CLUT = tiles.Select(x => x.PaletteID).Distinct().ToDictionary(x => x, x => new Color[colorsPerPalette]);
            using (BinaryReader br = new BinaryReader(new MemoryStream(mimb)))
                foreach (KeyValuePair<byte, Color[]> clut in CLUT)
                {
                    int palettePointer = textureType.BytesSkippedPalettes + ((clut.Key) * bytesPerPalette);
                    br.BaseStream.Seek(palettePointer, SeekOrigin.Begin);
                    for (int i = 0; i < colorsPerPalette; i++)
                        clut.Value[i] = Texture_Base.ABGR1555toRGBA32bit(br.ReadUInt16());
                }
            return CLUT;
        }

        private static void FindOverlappingTiles()
        {
            List<Tile[]> overlapIssues = (from t1 in tiles
                                          from t2 in tiles
                                          where t1.TileID < t2.TileID
                                          where t1.BlendMode == BlendMode.none
                                          where t1.Intersect(t2)
                                          orderby t1.TileID, t2.TileID ascending
                                          select new[] { t1, t2 }
                                      ).Distinct().ToList();
            foreach (Tile[] overlappingTiles in overlapIssues)
            {
                //if want pixel perfect test colors here.
                //you can tell if the two layers are okay if one is transparent when other isn't.
                overlappingTiles[1].OverLapID = checked((byte)(overlappingTiles[0].OverLapID + 1));
            }
        }

        private static void FindOverlappingTilesSource()
        {
            List<Tile[]> overlapIssues = (from t1 in tiles
                                          from t2 in tiles
                                          where t1.TileID < t2.TileID
                                          //where t1.BlendMode == BlendMode.none
                                          where t1.SourceIntersect(t2)
                                          orderby t1.TileID, t2.TileID ascending
                                          select new[] { t1, t2 }
                                      ).Distinct().ToList();
            foreach (Tile[] overlappingTiles in overlapIssues)
            {
                if (overlappingTiles[0].SourceRectangle() == overlappingTiles[1].SourceRectangle())
                {
                    overlappingTiles[1].Skip = true;
                }
                else
                {
                    overlappingTiles[1].SourceX = checked((ushort)((int)Math.Round(overlappingTiles[1].SourceX / (float)Tile.size) * Tile.size));
                    overlappingTiles[1].SourceY = checked((ushort)((int)Math.Round(overlappingTiles[1].SourceY / (float)Tile.size) * Tile.size));
                    overlappingTiles[0].SourceX = checked((ushort)((int)Math.Round(overlappingTiles[0].SourceX / (float)Tile.size) * Tile.size));
                    overlappingTiles[0].SourceY = checked((ushort)((int)Math.Round(overlappingTiles[0].SourceY / (float)Tile.size) * Tile.size));
                    if (overlappingTiles[0].SourceRectangle() == overlappingTiles[1].SourceRectangle())
                    {
                        overlappingTiles[1].Skip = true;
                    }
                }
                //if want pixel perfect test colors here.
                //you can tell if the two layers are okay if one is transparent when other isn't.
                //overlappingTiles[1].SourceOverLapID = checked((byte)(overlappingTiles[0].SourceOverLapID + 1));
            }
        }

        private static List<Tile> GetTiles(byte[] mapb, TextureType textureType)
        {
            tiles = new List<Tile>();
            //128x256
            //using (BinaryReader pbsmim = new BinaryReader(new MemoryStream(mimb))
            using (BinaryReader pbsmap = new BinaryReader(new MemoryStream(mapb)))
                while (pbsmap.BaseStream.Position + 16 < pbsmap.BaseStream.Length)
                {
                    Tile tile = new Tile { X = pbsmap.ReadInt16() };
                    if (tile.X == 0x7FFF)
                        break;
                    tile.Y = pbsmap.ReadInt16();
                    if (textureType.Type == 1)
                    {
                        tile.Z = pbsmap.ReadUInt16();// (ushort)(4096 - pbsmap.ReadUShort());
                        byte texIdBuffer = pbsmap.ReadByte();
                        tile.TextureID = (byte)(texIdBuffer & 0xF);
                        // pbsmap.BaseStream.Seek(-1, SeekOrigin.Current);
                        pbsmap.BaseStream.Seek(1, SeekOrigin.Current);
                        tile.PaletteID = (byte)((pbsmap.ReadInt16() >> 6) & 0xF);
                        tile.SourceX = pbsmap.ReadByte();
                        tile.SourceY = pbsmap.ReadByte();
                        tile.LayerID = (byte)(pbsmap.ReadByte() >> 1/*& 0x7F*/);
                        tile.BlendMode = (BlendMode)pbsmap.ReadByte();
                        tile.AnimationID = pbsmap.ReadByte();
                        tile.AnimationState = pbsmap.ReadByte();
                        tile.blend1 = (byte)((texIdBuffer >> 4) & 0x1);
                        tile.Depth = (byte)(texIdBuffer >> 5);
                        tile.TileID = tiles.Count;
                    }
                    else if(textureType.Type == 2)
                    {
                        tile.SourceX = pbsmap.ReadUInt16();
                        tile.SourceY = pbsmap.ReadUInt16();
                        tile.Z = pbsmap.ReadUInt16();
                        byte texIdBuffer = pbsmap.ReadByte();
                        tile.TextureID = (byte)(texIdBuffer & 0xF);
                        pbsmap.BaseStream.Seek(1, SeekOrigin.Current);
                        tile.PaletteID = (byte)((pbsmap.ReadInt16() >> 6) & 0xF);
                        tile.AnimationID = pbsmap.ReadByte();
                        tile.AnimationState = pbsmap.ReadByte();
                        tile.blend1 = (byte)((texIdBuffer >> 4) & 0x1);
                        tile.Depth = (byte)(texIdBuffer >> 5);
                        tile.TileID = tiles.Count;
                    }
                    tiles.Add(tile);
                    
                }
            return tiles;
        }

        private static byte GetColorKey(byte[] mimb, int textureWidth, int startPixel, int x, int y, bool is8Bit)
        {
            if (is8Bit)
                return mimb[startPixel + x + (y * textureWidth)];
            else
            {
                byte tempKey = mimb[startPixel + x / 2 + (y * textureWidth)];
                if (x % 2 == 1)
                    return checked((byte)((tempKey & 0xf0) >> 4));
                else
                    return checked((byte)(tempKey & 0xf));
            }
        }

        private static void SaveTextures()
        {
            if (Memory.EnableDumpingData)
            {
                //List<KeyValuePair<BlendModes, Texture2D>> _drawtextures = drawtextures();
                foreach (KeyValuePair<ushort, ConcurrentDictionary<byte, ConcurrentDictionary<byte, ConcurrentDictionary<byte, ConcurrentDictionary<byte, ConcurrentDictionary<BlendMode, Texture2D>>>>>> kvp_Z in Textures)
                    foreach (KeyValuePair<byte, ConcurrentDictionary<byte, ConcurrentDictionary<byte, ConcurrentDictionary<byte, ConcurrentDictionary<BlendMode, Texture2D>>>>> kvp_Layer in kvp_Z.Value)
                        foreach (KeyValuePair<byte, ConcurrentDictionary<byte, ConcurrentDictionary<byte, ConcurrentDictionary<BlendMode, Texture2D>>>> kvp_AnimationID in kvp_Layer.Value)
                            foreach (KeyValuePair<byte, ConcurrentDictionary<byte, ConcurrentDictionary<BlendMode, Texture2D>>> kvp_AnimationState in kvp_AnimationID.Value)
                                foreach (KeyValuePair<byte, ConcurrentDictionary<BlendMode, Texture2D>> kvp_OverlapID in kvp_AnimationState.Value)
                                    foreach (KeyValuePair<BlendMode, Texture2D> kvp in kvp_OverlapID.Value)
                                    {
                                        string path = Path.Combine(Path.GetTempPath(), "Fields", Memory.FieldHolder.FieldID.ToString());
                                        Directory.CreateDirectory(path);
                                        using (FileStream fs = new FileStream(Path.Combine(path,
                                            $"Field.{Memory.FieldHolder.FieldID}.{kvp_Z.Key.ToString("D4")}.{kvp_Layer.Key}.{kvp_AnimationID.Key}.{kvp_AnimationState.Key}.{kvp_OverlapID.Key}.{(int)kvp.Key}.png"),
                                            FileMode.Create, FileAccess.Write, FileShare.ReadWrite))
                                            kvp.Value.SaveAsPng(
                                                fs,
                                                kvp.Value.Width, kvp.Value.Height);
                                    }
            }
        }

        private static void DrawEntities() => throw new NotImplementedException();

        ///// <summary>
        ///// Blend the colors depending on tile.blendmode
        ///// </summary>
        ///// <param name="finalImageColor"></param>
        ///// <param name="color"></param>
        ///// <param name="tile"></param>
        ///// <returns>Color</returns>
        ///// <see cref="http://www.raphnet.net/electronique/psx_adaptor/Playstation.txt"/>
        ///// <seealso cref="http://www.psxdev.net/forum/viewtopic.php?t=953"/>
        ///// <seealso cref="//http://wiki.ffrtt.ru/index.php?title=FF8/FileFormat_MAP"/>
        //private static Color BlendColors(ref Color finalImageColor, Color color, Tile tile)
        //{
        //    //• Semi Transparency
        //    //When semi transparency is set for a pixel, the GPU first reads the pixel it wants to write to, and then calculates
        //    //the color it will write from the 2 pixels according to the semi - transparency mode selected.Processing speed is lower
        //    //in this mode because additional reading and calculating are necessary.There are 4 semi - transparency modes in the
        //    //GPU.
        //    //B = the pixel read from the image in the frame buffer, F = the half transparent pixel
        //    //• 1.0 x B + 0.5 x F
        //    //• 1.0 x B + 1.0 x F
        //    //• 1.0 x B - 1.0 x F
        //    //• 1.0 x B + 0.25 x F
        //    //color must not be black
        //    Color baseColor = finalImageColor;
        //    //BlendState blendmode1 = new BlendState
        //    //{
        //    //    ColorSourceBlend = Blend.SourceColor,
        //    //    ColorDestinationBlend = Blend.DestinationColor,
        //    //    ColorBlendFunction = BlendFunction.Add
        //    //};
        //    //BlendState blendmode2 = new BlendState
        //    //{
        //    //    ColorSourceBlend = Blend.SourceColor,
        //    //    ColorDestinationBlend = Blend.DestinationColor,
        //    //    ColorBlendFunction = BlendFunction.Subtract
        //    //};
        //    //BlendState blendmode3 = new BlendState
        //    //{
        //    //    BlendFactor =
        //    //    ColorSourceBlend = Blend.SourceColor,
        //    //    ColorDestinationBlend = Blend.DestinationColor,
        //    //    ColorBlendFunction = BlendFunction.Subtract
        //    //};

        //    switch (tile.BlendMode)
        //    {
        //        case BlendMode.halfadd:
        //            return finalImageColor = blend0(baseColor, color);

        //        case BlendMode.add:
        //            return finalImageColor = blend1(baseColor, color);

        //        case BlendMode.subtract:
        //            return finalImageColor = blend2(baseColor, color);

        //        case BlendMode.quarteradd:
        //            //break;
        //            return finalImageColor = blend3(baseColor, color);
        //    }
        //    throw new Exception($"Blendtype is {tile.BlendMode}: There are only 4 blend modes, 0-3, 4+ are drawn directly.");
        //}

        private static Color blend0(Color baseColor, Color color)
        {
            Color r;
            r.R = (byte)MathHelper.Clamp(baseColor.R + color.R / 2, 0, 255);
            r.G = (byte)MathHelper.Clamp(baseColor.R + color.G / 2, 0, 255);
            r.B = (byte)MathHelper.Clamp(baseColor.B + color.B / 2, 0, 255);
            r.A = 0xFF;
            return r;
        }

        private static Color blend1(Color baseColor, Color color)
        {
            Color r;
            r.R = (byte)MathHelper.Clamp(baseColor.R + color.R, 0, 255);
            r.G = (byte)MathHelper.Clamp(baseColor.G + color.G, 0, 255);
            r.B = (byte)MathHelper.Clamp(baseColor.B + color.B, 0, 255);
            r.A = 0xFF;
            return r;
        }

        private static Color blend2(Color baseColor, Color color)
        {
            Color r;
            r.R = (byte)MathHelper.Clamp(baseColor.R - color.R, 0, 255);
            r.G = (byte)MathHelper.Clamp(baseColor.G - color.G, 0, 255);
            r.B = (byte)MathHelper.Clamp(baseColor.B - color.B, 0, 255);
            r.A = 0xFF;
            return r;
        }

        private static Color blend3(Color baseColor, Color color)
        {
            Color r;
            r.R = (byte)MathHelper.Clamp((byte)(baseColor.R + (color.R / 4)), 0, 255);
            r.G = (byte)MathHelper.Clamp((byte)(baseColor.G + (color.G / 4)), 0, 255);
            r.B = (byte)MathHelper.Clamp((byte)(baseColor.B + (color.B / 4)), 0, 255);
            r.A = 0xFF;
            return r;
        }
    }
}