// See-through: anything standing between the camera and a hero dithers away around that hero, so the hero stays visible.
// Used by the shaders of walls, roofs and trees. Only the visible surface is cut: shadows and collision do not change.
#ifndef TWF_REVEAL_INCLUDED
#define TWF_REVEAL_INCLUDED

#define TWF_REVEAL_MAX 4
float4 _TWF_Reveal[TWF_REVEAL_MAX];   // xyz = where a hero stands
float _TWF_RevealCount;

static const float TWF_Bayer4[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };

// positionWS: the pixel. groundWS: where it stands for sorting (the pixel itself, or its spot on the ground).
void TWF_RevealClip(float3 positionWS, float3 groundWS, float4 positionCS)
{
    if (_TWF_RevealCount < 0.5) return;
    float3 forward = -UNITY_MATRIX_V[2].xyz;
    float3 up = UNITY_MATRIX_V[1].xyz;
    float amount = 0;
    for (int i = 0; i < TWF_REVEAL_MAX && i < _TWF_RevealCount; i++)
    {
        float3 hero = _TWF_Reveal[i].xyz;
        // What the hero stands on never fades: steps under the feet, the cliff below a plateau's edge (up is -Z).
        if (positionWS.z > hero.z - 0.2) continue;
        // In front of the hero, along the view? Starts just before the hero is fully behind, so nothing pops.
        float front = saturate((dot(hero - groundWS, forward) + 0.1) / 0.2);
        if (front <= 0) continue;
        // Close to the hero on screen?
        float3 q = positionWS - (hero + up);
        q -= forward * dot(q, forward);
        float r = length(q);
        amount = max(amount, front * saturate(1.0 - (r - 1.1) / 0.9));
    }
    if (amount <= 0) return;
    uint x = (uint)positionCS.x % 4, y = (uint)positionCS.y % 4;
    float threshold = (TWF_Bayer4[y * 4 + x] + 0.5) / 16.0;
    clip(threshold - amount * 0.82);
}

#endif
