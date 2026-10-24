using System;
using UnityEngine;

namespace TacticalMaps.Grass
{
    internal enum GrassFlowerForm { Rosette, Cup, Bell, Star }
    [Serializable]
    internal sealed class GrassFlowerType
    {
        internal const int Maximum = 8;
        public string name;
        [ColorUsage(false)] public Color color;
        [Min(0)] public float weight;
        [Range(.5f,2)] public float size;
        public GrassFlowerForm form;
        internal GrassFlowerType(string label,Color tint,float frequency,float scale=1,GrassFlowerForm shape=GrassFlowerForm.Rosette)
        {name=label;color=tint;weight=frequency;size=scale;form=shape;}
    }
}
