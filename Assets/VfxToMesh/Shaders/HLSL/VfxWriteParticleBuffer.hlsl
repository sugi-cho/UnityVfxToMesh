
uint WrapParticleIndex(uint particleId, uint particleCapacity)
{
    return particleCapacity == 0 ? 0 : particleId % max(1u, particleCapacity);
}

void WriteParticleBufferBlock(inout VFXAttributes attributes,
                              RWStructuredBuffer<float4> particleBuffer,
                              uint particleCapacity)
{
    uint index = WrapParticleIndex((uint)attributes.particleId, particleCapacity);

    if (attributes.alive == 0)
    {
        particleBuffer[index] = float4(0, 0, 0, -1);
        return;
    }

    float radius = max(attributes.size, 0.0001f) * 0.5f;
    particleBuffer[index] = float4(attributes.position, radius);
}

void WriteParticleColorBlock(inout VFXAttributes attributes,
                             RWStructuredBuffer<float4> particleColorBuffer,
                             uint particleCapacity)
{
    uint index = WrapParticleIndex((uint)attributes.particleId, particleCapacity);

    if (attributes.alive == 0)
    {
        particleColorBuffer[index] = float4(0, 0, 0, 0);
        return;
    }

    float radius = max(attributes.size, 0.0001f);
    float weight = saturate(attributes.alpha * radius);
    particleColorBuffer[index] = float4(attributes.tint.rgb, weight);
}
