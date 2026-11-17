#ifndef AR_VISUALIZER_MOUNTED_SCREEN_DEPTH_INCLUDED
#define AR_VISUALIZER_MOUNTED_SCREEN_DEPTH_INCLUDED

// A fitted AR plane and the measured wall depth need not coincide exactly. Allow a
// small metric tolerance for mounted content, without moving its vertices, UVs or
// interaction plane. Using the projection matrix handles reversed Z and both eye
// projections; a constant clip-space offset would change with viewing distance.
float4 MountedScreenClipPosition(float4 clipPosition, float3 viewPosition, float toleranceMetres)
{
    float eyeDepth = -viewPosition.z;
    if (eyeDepth <= _ProjectionParams.y || toleranceMetres <= 0) return clipPosition;
    viewPosition.z = -max(_ProjectionParams.y, eyeDepth - min(toleranceMetres, 0.05));
    float4 tolerated = mul(UNITY_MATRIX_P, float4(viewPosition, 1));
    clipPosition.z = tolerated.z / tolerated.w * clipPosition.w;
    return clipPosition;
}

#endif
