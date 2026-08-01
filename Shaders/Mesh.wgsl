struct MeshUniforms {
    model: mat4x4<f32>,
    view: mat4x4<f32>,
    proj: mat4x4<f32>,
    color: vec4<f32>,
    lightDir: vec3<f32>,
    isSelected: u32,
};

@group(0) @binding(0) var<uniform> u: MeshUniforms;

struct VertexInput {
    @location(0) position: vec3<f32>,
    @location(1) normal: vec3<f32>,
};

struct VertexOutput {
    @builtin(position) clip_position: vec4<f32>,
    @location(0) frag_pos: vec3<f32>,
    @location(1) normal: vec3<f32>,
};

@vertex
fn vs_main(in: VertexInput) -> VertexOutput {
    var out: VertexOutput;
    let world_pos = u.model * vec4<f32>(in.position, 1.0);
    out.frag_pos = world_pos.xyz;

    let norm_mat = mat3x3<f32>(u.model[0].xyz, u.model[1].xyz, u.model[2].xyz);
    out.normal = norm_mat * in.normal;
    out.clip_position = u.proj * u.view * world_pos;
    return out;
}

@vertex
fn vs_outline(in: VertexInput) -> VertexOutput {
    var out: VertexOutput;
    let world_pos = u.model * vec4<f32>(in.position * 1.06, 1.0);
    out.frag_pos = world_pos.xyz;

    let norm_mat = mat3x3<f32>(u.model[0].xyz, u.model[1].xyz, u.model[2].xyz);
    out.normal = norm_mat * in.normal;
    out.clip_position = u.proj * u.view * world_pos;
    return out;
}

@fragment
fn fs_main(in: VertexOutput) -> @location(0) vec4<f32> {
    let norm = normalize(in.normal);
    let light = normalize(u.lightDir);
    let diff = max(dot(norm, light), 0.25);
    var base_color = u.color.rgb * diff;
    if (u.isSelected != 0u) {
        return vec4<f32>(1.0, 0.72, 0.08, 1.0);
    }
    return vec4<f32>(base_color, u.color.a);
}

@fragment
fn fs_outline(in: VertexOutput) -> @location(0) vec4<f32> {
    return vec4<f32>(1.0, 0.72, 0.08, 1.0);
}
