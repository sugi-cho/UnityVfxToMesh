// Pack strip points from a VFX Custom HLSL Block into a GraphicsBuffer.
// Each strip owns a point array; Compute side connects point[i-1] and point[i] to form segments.

#ifndef VFX_WRITE_STRIP_BUFFER_INCLUDED
#define VFX_WRITE_STRIP_BUFFER_INCLUDED

// Write rule:
//   writeIndex = stripIndex * pointsPerStrip + indexPerStrip
//   indexPerStrip == 0 is also written (used as tail of the next segment)
//   stripIndex / indexPerStrip are read from Particle Strip built-in attributes.
// Packing:
//   slot0: float4(position, radius)
//   slot1: float4(color.rgb, weight)
void WriteStripPointBlock(inout VFXAttributes attributes,
                                 RWStructuredBuffer<float4> pointBuffer,
                                 uint pointsPerStrip,
                                 uint stripCount)
{
    if (attributes.alive == 0)
        return;

    uint stripIndex = attributes.stripIndex;
    uint indexPerStrip = attributes.particleIndexInStrip;

    if (stripIndex >= stripCount || indexPerStrip >= pointsPerStrip)
        return; // out of capacity

    uint writeIndex = stripIndex * pointsPerStrip + indexPerStrip;
    uint baseIdx = writeIndex * 2; // two float4 per point

    float radius = max(attributes.size, 0.0001f) * 0.5f;
    float weight = attributes.alpha;

    pointBuffer[baseIdx + 0] = float4(attributes.position, radius);
    pointBuffer[baseIdx + 1] = float4(attributes.color.rgb, weight);

}

#endif // VFX_WRITE_STRIP_BUFFER_INCLUDED
