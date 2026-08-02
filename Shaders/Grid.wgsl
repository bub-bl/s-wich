struct GridUniforms {
    view: mat4x4<f32>,
    proj: mat4x4<f32>,
    viewInv: mat4x4<f32>,
    projInv: mat4x4<f32>,
    settings: vec4<f32>,
    lineColor: vec4<f32>,
    xAxisColor: vec4<f32>,
    zAxisColor: vec4<f32>,
};

@group(0) @binding(0) var<uniform> gu: GridUniforms;

struct VertexOutput {
    @builtin(position) clip_position: vec4<f32>,
    @location(0) near_point: vec3<f32>,
    @location(1) far_point: vec3<f32>,
};

fn unprojectPoint(x: f32, y: f32, z: f32) -> vec3<f32> {
    let p = gu.viewInv * gu.projInv * vec4<f32>(x, y, z, 1.0);
    return p.xyz / p.w;
}

@vertex
fn vs_main(@location(0) position: vec3<f32>) -> VertexOutput {
    var out: VertexOutput;
    out.clip_position = vec4<f32>(position.xy, 0.0, 1.0);
    out.near_point = unprojectPoint(position.x, position.y, 0.0);
    out.far_point = unprojectPoint(position.x, position.y, 1.0);
    return out;
}

struct FragmentOutput {
    @location(0) color: vec4<f32>,
    @builtin(frag_depth) depth: f32,
};

@fragment
fn fs_main(in: VertexOutput) -> FragmentOutput {
    let t = -in.near_point.y / (in.far_point.y - in.near_point.y);
    if (t < 0.0 || t > 1.0) {
        discard;
    }

    let fragPos3D = in.near_point + t * (in.far_point - in.near_point);
    if (gu.settings.x > 0.0) {
        let halfSize = gu.settings.x * 0.5;
        if (abs(fragPos3D.x) > halfSize || abs(fragPos3D.z) > halfSize) {
            discard;
        }
    }
    let clipSpacePos = gu.proj * gu.view * vec4<f32>(fragPos3D, 1.0);
    let realDepth = clipSpacePos.z / clipSpacePos.w;
    // The grid lies exactly on the ground plane, so its depth can match an
    // object's depth at the contact edge.  Nudge it forward by a fraction of
    // a pixel to make the depth comparison deterministic and avoid z-fighting
    // without making the grid ignore objects that are actually in front of it.
    let depthBias = max(fwidth(realDepth) * 0.5, 0.000001);

    let coord = fragPos3D.xz / gu.settings.y;
    let derivative = fwidth(coord);
    let grid = abs(fract(coord - 0.5) - 0.5) / derivative;
    let line = min(grid.x, grid.y);
    // Keep the main axes visible as continuous one-pixel lines.  Using a
    // tiny fraction of the pixel footprint here makes the axes hit only
    // occasional fragments, which shows up as red/blue dots along the grid.
    let minimumz = min(derivative.y, 1.0);
    let minimumx = min(derivative.x, 1.0);
    let onXAxis = abs(fragPos3D.x) <= minimumx;
    let onZAxis = abs(fragPos3D.z) <= minimumz;
    // The grid is transparent between its lines.  Discarding those pixels is
    // important because the grid is rendered after the meshes and must not
    // cover objects through an otherwise invisible fragment.
    if (line > 1.0 && !onXAxis && !onZAxis) {
        discard;
    }

    var gridColor = gu.lineColor * vec4<f32>(1.0, 1.0, 1.0, 1.0 - min(line, 1.0));

    if (onXAxis && gu.settings.w > 0.5) {
        gridColor = gu.xAxisColor;
    }
    if (onZAxis && gu.settings.w > 0.5) {
        gridColor = gu.zAxisColor;
    }

    let fading = max(0.0, 1.0 - length(fragPos3D.xz) / gu.settings.z);

    var out: FragmentOutput;
    out.color = gridColor * fading;
    out.depth = max(realDepth - depthBias, 0.0);
    return out;
}
