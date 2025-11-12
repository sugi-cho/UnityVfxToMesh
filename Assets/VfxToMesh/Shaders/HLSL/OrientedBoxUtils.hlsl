#ifndef ORIENTED_BOX_UTILS_HLSL
#define ORIENTED_BOX_UTILS_HLSL

float3x3 RotationX(float angle)
{
    float c = cos(angle);
    float s = sin(angle);
    return float3x3(
        1,  0,  0,
        0,  c, -s,
        0,  s,  c);
}

float3x3 RotationY(float angle)
{
    float c = cos(angle);
    float s = sin(angle);
    return float3x3(
         c, 0, s,
         0, 1, 0,
        -s, 0, c);
}

float3x3 RotationZ(float angle)
{
    float c = cos(angle);
    float s = sin(angle);
    return float3x3(
        c, -s, 0,
        s,  c, 0,
        0,  0, 1);
}

/// @param worldPos   world space sample position
/// @param boxCenter  oriented box center in world space
/// @param boxAngles  Euler angles in degrees
/// @param boxSize    full size along each axis
float3 ComputeOrientedBoxUVW(float3 worldPos, float3 boxCenter, float3 boxAngles, float3 boxSize)
{
    float3 offset = worldPos - boxCenter;
    float3 radiansAngles = radians(boxAngles);

    float3x3 rotation =
        mul(mul(RotationZ(radiansAngles.z), RotationY(radiansAngles.y)), RotationX(radiansAngles.x));

    float3x3 worldToLocal = transpose(rotation);
    float3 localPos = mul(worldToLocal, offset);

    float3 safeSize = max(boxSize, float3(1e-5, 1e-5, 1e-5));
    float3 normalized = localPos / safeSize + 0.5;
    return saturate(normalized);
}

#endif // ORIENTED_BOX_UTILS_HLSL
