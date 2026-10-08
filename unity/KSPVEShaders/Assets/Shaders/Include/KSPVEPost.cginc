#ifndef KSPVE_POST_INCLUDED
#define KSPVE_POST_INCLUDED

// Globals shared by KSPVE post-processing passes (set from PostStack.cs with Shader.SetGlobal*).
float _KSPVE_ExposureMul;          // 2^EV (and the ACES pre-scale)
float _KSPVE_UseAutoExposure;      // 0 or 1
sampler2D _KSPVE_AutoExposureTex;  // 1x1 RFloat: log2 exposure multiplier

float KSPVEExposure()
{
    float e = _KSPVE_ExposureMul;
    if (_KSPVE_UseAutoExposure > 0.5)
        e *= exp2(tex2Dlod(_KSPVE_AutoExposureTex, float4(0.5, 0.5, 0, 0)).r);
    return e;
}

#endif
