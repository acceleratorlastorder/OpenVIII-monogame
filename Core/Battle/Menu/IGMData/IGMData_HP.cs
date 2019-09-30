﻿using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenVIII
{
    public partial class BattleMenu
    {
        #region Classes

        private class IGMData_HP : IGMData
        {
            #region Fields

            private static Texture2D dot;
            private Mode mode;

            #endregion Fields

            #region Constructors

            public IGMData_HP(Rectangle pos, Characters character, Characters Visiblecharacter) : base(3, 4, new IGMDataItem_Empty(pos), 1, 3, character, Visiblecharacter)
            {
            }

            #endregion Constructors

            #region Methods

            public override void Refresh()
            {
                if (Memory.State?.Characters != null)
                {
                    List<KeyValuePair<int, Characters>> party = GetParty();
                    byte pos = GetCharPos(party);
                    foreach (KeyValuePair<int, Characters> pm in party.Where(x => x.Value == VisibleCharacter))
                    {
                        Saves.CharacterData c = Memory.State.Characters[Memory.State.PartyData[pm.Key]];
                        FF8String name = Memory.Strings.GetName(pm.Value);
                        int HP = c.CurrentHP(pm.Value);
                        //int MaxHP = c.MaxHP(pm.Value);
                        //float HPpercent = c.PercentFullHP(pm.Value);
                        int CriticalHP = c.CriticalHP(pm.Value);
                        Font.ColorID colorid = Font.ColorID.White;
                        byte palette = 2;                        
                        if (HP < CriticalHP)
                        {
                            colorid = Font.ColorID.Yellow;
                            //palette = 6;
                        }
                        if (HP <= 0)
                        {
                            colorid = Font.ColorID.Red;
                            //palette = 5;
                        }
                        bool blink = false;
                        Rectangle atbbarpos = new Rectangle(SIZE[pos].X + 230, SIZE[pos].Y + 12, 150, 15);
                        if (mode == Mode.YourTurn)
                        {
                            blink = true;
                            ITEM[pos, 2] = new IGMDataItem_Texture(dot, atbbarpos, Color.LightYellow * .8f, new Color(125, 125, 0, 255) * .8f) { Blink = blink };
                        }
                        else if (mode == Mode.ATB_Charged)
                            ITEM[pos, 2] = new IGMDataItem_Texture(dot, atbbarpos, Color.Yellow * .8f);
                        // insert gradient atb bar here. Though this probably belongs in the update
                        // method as it'll be in constant flux.

                        else if (ITEM[pos, 2]?.GetType() != typeof(IGMDataItem_ATB_Gradient_old))
                        {
                            ITEM[pos, 2] = new IGMDataItem_ATB_Gradient(atbbarpos);
                            ((IGMDataItem_ATB_Gradient)ITEM[pos, 2]).Color = Color.Orange * .8f;
                            ((IGMDataItem_ATB_Gradient)ITEM[pos, 2]).Faded_Color = Color.Orange * .8f;
                            ((IGMDataItem_ATB_Gradient)ITEM[pos, 2]).Refresh(Character, VisibleCharacter);
                        }

                        // TODO: make a font render that can draw right to left from a point. For Right aligning the names.
                        ITEM[pos, 0] = new IGMDataItem_String(name, new Rectangle(SIZE[pos].X, SIZE[pos].Y, 0, 0), colorid) { Blink = blink };
                        ITEM[pos, 1] = new IGMDataItem_Int(HP, new Rectangle(SIZE[pos].X + 128, SIZE[pos].Y, 0, 0), palette: palette, spaces: 4, numtype: Icons.NumType.Num_8x16_1,fontcolor: colorid) { Blink = blink };

                        ITEM[pos, 3] = new IGMDataItem_Icon(Icons.ID.Size_08x64_Bar, atbbarpos, 0);
                        pos++;
                    }
                    base.Refresh();
                }
            }
            public override bool Update()
            {
                List<KeyValuePair<int, Characters>> party = GetParty();
                byte pos = GetCharPos(party);
                if (ITEM[pos, 2].GetType() == typeof(IGMDataItem_ATB_Gradient))
                {
                    var hg = (IGMDataItem_ATB_Gradient)ITEM[pos, 2];
                    if (hg.Done)
                    {
                        var cm = BattleMenus.GetBattleMenus().First(x => x.VisibleCharacter == VisibleCharacter);
                        cm.SetMode(Mode.ATB_Charged);
                    }
                }
                return base.Update();
            }

            private byte GetCharPos(List<KeyValuePair<int, Characters>> party) => (byte)party.FindIndex(x => x.Value == VisibleCharacter);
            private static List<KeyValuePair<int, Characters>> GetParty() => Memory.State.Party.Select((element, index) => new { element, index }).ToDictionary(m => m.index, m => m.element).Where(m => !m.Value.Equals(Characters.Blank)).ToList();

            protected override void Init()
            {
                if (dot == null)
                {
                    dot = new Texture2D(Memory.graphics.GraphicsDevice, 1, 1);
                    lock (dot)
                        dot.SetData(new Color[] { Color.White });
                }
                base.Init();
            }

            protected override void ModeChangeEvent(object sender, Enum e)
            {
                base.ModeChangeEvent(sender, e);
                if (e.GetType() == typeof(Mode))
                {
                    mode = (Mode)e;
                    Refresh();
                }
            }

            #endregion Methods
        }

        #endregion Classes
    }
}