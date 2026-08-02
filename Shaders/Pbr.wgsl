struct PbrUniforms {
    model: mat4x4<f32>,
    view: mat4x4<f32>,
    proj: mat4x4<f32>,
    color: vec4<f32>,
    lightDir: vec3<f32>,
    isSelected: u32,
    materialFactors: vec4<f32>, // x metallic, y roughness, z occlusion, w emissive
    cameraPosition: vec4<f32>,
};

@group(0) @binding(0) var<uniform> u: PbrUniforms;
@group(1) @binding(0) var materialSampler: sampler;
@group(1) @binding(1) var albedoTexture: texture_2d<f32>;
@group(1) @binding(2) var normalTexture: texture_2d<f32>;
@group(1) @binding(3) var metallicRoughnessTexture: texture_2d<f32>;
@group(1) @binding(4) var occlusionTexture: texture_2d<f32>;
@group(1) @binding(5) var emissiveTexture: texture_2d<f32>;

struct VertexInput {
    @location(0) position: vec3<f32>,
    @location(1) normal: vec3<f32>,
    @location(2) tangent: vec4<f32>,
    @location(3) uv: vec2<f32>,
};

struct VertexOutput {
    @builtin(position) clip_position: vec4<f32>,
    @location(0) worldPosition: vec3<f32>,
    @location(1) normal: vec3<f32>,
    @location(2) tangent: vec4<f32>,
    @location(3) uv: vec2<f32>,
};

@vertex
fn vs_main(input: VertexInput) -> VertexOutput {
    var output: VertexOutput;
    let worldPosition = u.model * vec4<f32>(input.position, 1.0);
    let normalMatrix = mat3x3<f32>(u.model[0].xyz, u.model[1].xyz, u.model[2].xyz);
    output.worldPosition = worldPosition.xyz;
    output.normal = normalize(normalMatrix * input.normal);
    output.tangent = vec4<f32>(normalize(normalMatrix * input.tangent.xyz), input.tangent.w);
    output.uv = input.uv;
    output.clip_position = u.proj * u.view * worldPosition;
    return output;
}

fn fresnelSchlick(cosine: f32, f0: vec3<f32>) -> vec3<f32> {
    return f0 + (vec3<f32>(1.0) - f0) * pow(1.0 - cosine, 5.0);
}

fn distributionGgx(n: vec3<f32>, h: vec3<f32>, roughness: f32) -> f32 {
    let a = roughness * roughness;
    let a2 = a * a;
    let ndoth = max(dot(n, h), 0.0);
    let denominator = ndoth * ndoth * (a2 - 1.0) + 1.0;
    return a2 / max(3.14159265 * denominator * denominator, 0.0001);
}

fn geometrySchlick(cosine: f32, roughness: f32) -> f32 {
    let k = (roughness + 1.0) * (roughness + 1.0) / 8.0;
    return cosine / max(cosine * (1.0 - k) + k, 0.0001);
}

@fragment
fn fs_main(input: VertexOutput) -> @location(0) vec4<f32> {
    let albedoSample = textureSample(albedoTexture, materialSampler, input.uv);
    let normalSample = textureSample(normalTexture, materialSampler, input.uv).xyz * 2.0 - 1.0;
    let metallicRoughness = textureSample(metallicRoughnessTexture, materialSampler, input.uv);
    let occlusion = textureSample(occlusionTexture, materialSampler, input.uv).r;
    let emissive = textureSample(emissiveTexture, materialSampler, input.uv).rgb;

    let n = normalize(input.normal);
    let t = normalize(input.tangent.xyz - n * dot(n, input.tangent.xyz));
    let b = normalize(cross(n, t) * input.tangent.w);
    let mappedNormal = normalize(t * normalSample.x + b * normalSample.y + n * normalSample.z);
    let baseColor = albedoSample.rgb * u.color.rgb;
    let metallic = clamp(metallicRoughness.b * u.materialFactors.x, 0.0, 1.0);
    let roughness = clamp(metallicRoughness.g * u.materialFactors.y, 0.045, 1.0);
    let light = normalize(u.lightDir);
    let view = normalize(u.cameraPosition.xyz - input.worldPosition);
    let halfVector = normalize(light + view);
    let ndotl = max(dot(mappedNormal, light), 0.0);
    let ndotv = max(dot(mappedNormal, view), 0.0);
    let hdotv = max(dot(halfVector, view), 0.0);
    let f0 = mix(vec3<f32>(0.04), baseColor, metallic);
    let fresnel = fresnelSchlick(hdotv, f0);
    let specular = distributionGgx(mappedNormal, halfVector, roughness)
        * geometrySchlick(ndotl, roughness) * geometrySchlick(ndotv, roughness)
        * fresnel / max(4.0 * ndotl * ndotv, 0.001);
    let diffuse = (vec3<f32>(1.0) - fresnel) * (1.0 - metallic) * baseColor / 3.14159265;
    let lit = (diffuse + specular) * ndotl * occlusion;
    let color = lit + baseColor * 0.16 * u.materialFactors.z + emissive * u.materialFactors.w;

    if (u.isSelected != 0u) {
        return vec4<f32>(1.0, 0.72, 0.08, u.color.a);
    }
    return vec4<f32>(pow(max(color, vec3<f32>(0.0)), vec3<f32>(1.0 / 2.2)), albedoSample.a * u.color.a);
}
