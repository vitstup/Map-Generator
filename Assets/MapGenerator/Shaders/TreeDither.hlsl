#ifndef TACTICAL_TREE_DITHER_INCLUDED
#define TACTICAL_TREE_DITHER_INCLUDED
// Complementary masks for the two adjacent representations. Stable without TAA.
float TreeDitherNoise(float2 pixel)
{
    return frac(52.9829189 * frac(dot(floor(pixel), float2(0.06711056, 0.00583715))));
}
void TreeDitherClip(float2 pixel, float fade)
{
    if (fade < 0.9999)
    {
        float noise = TreeDitherNoise(pixel);
        clip(fade >= 0 ? fade - noise : noise + fade);
    }
}
#endif
