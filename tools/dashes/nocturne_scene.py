"""Blender (Cycles) scene for the NOCTURNE dash background. Run headless, from make_nocturne.py:

    blender -b -P nocturne_scene.py -- scene.json out.png

The dash is built as real geometry (bevelled slabs, a lathed lens with sloped walls and a glowing neon tube, glass
cylinders, raised pads) and rendered straight down with an orthographic camera, so one pixel of the picture is one pixel
of the dash and the values the screen draws line up exactly. Depth comes from real light: soft shadows from a key sun
at the top left, bounce light from the neon, reflections in the metal, glow (compositor) round the bright parts.

Coordinates in scene.json are dash pixels (x right, y down); the scene is built with y up (y' = H - y), z toward the camera.
"""
import bpy, bmesh, json, math, sys
from mathutils import Vector

argv = sys.argv[sys.argv.index('--') + 1:]
S = json.load(open(argv[0]))
OUT = argv[1]
W, H = S['W'], S['H']
SS = S.get('ss', 2)

bpy.ops.wm.read_factory_settings(use_empty=True)
sc = bpy.context.scene


# ---------------------------------------------------------------- colour and materials
def lin(c):
    c = c / 255.0
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def rgb(c):
    return (lin(c[0]), lin(c[1]), lin(c[2]), 1.0)


def principled(name, base, metallic=0.0, rough=0.5, spec=0.5, aniso=0.0, emission=None, estrength=0.0, **extra):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    n = m.node_tree.nodes['Principled BSDF']
    n.inputs['Base Color'].default_value = rgb(base)
    n.inputs['Metallic'].default_value = metallic
    n.inputs['Roughness'].default_value = rough
    n.inputs['Specular IOR Level'].default_value = spec
    if aniso:
        n.inputs['Anisotropic'].default_value = aniso
        tg = m.node_tree.nodes.new('ShaderNodeTangent')
        tg.direction_type = 'RADIAL'
        tg.axis = 'Z'
        m.node_tree.links.new(tg.outputs['Tangent'], n.inputs['Tangent'])
    if emission is not None:
        n.inputs['Emission Color'].default_value = rgb(emission)
        n.inputs['Emission Strength'].default_value = estrength
    for k, v in extra.items():
        n.inputs[k.replace('_', ' ')].default_value = v
    return m


def emissive(name, col, strength=1.0):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    nt.nodes.clear()
    e = nt.nodes.new('ShaderNodeEmission')
    e.inputs['Color'].default_value = rgb(col)
    e.inputs['Strength'].default_value = strength
    o = nt.nodes.new('ShaderNodeOutputMaterial')
    nt.links.new(e.outputs[0], o.inputs[0])
    return m


def image_material(name, path, emission_strength=1.0, diffuse=False):
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    img = nt.nodes.new('ShaderNodeTexImage')
    img.image = bpy.data.images.load(path)
    img.image.colorspace_settings.name = 'sRGB'
    img.extension = 'EXTEND'
    if diffuse:
        n = nt.nodes['Principled BSDF']
        nt.links.new(img.outputs['Color'], n.inputs['Base Color'])
        nt.links.new(img.outputs['Color'], n.inputs['Emission Color'])
        n.inputs['Emission Strength'].default_value = emission_strength
        n.inputs['Roughness'].default_value = 0.6
        n.inputs['Specular IOR Level'].default_value = 0.2
    else:
        nt.nodes.remove(nt.nodes['Principled BSDF'])
        e = nt.nodes.new('ShaderNodeEmission')
        e.inputs['Strength'].default_value = emission_strength
        o = nt.nodes['Material Output']
        nt.links.new(img.outputs['Color'], e.inputs['Color'])
        nt.links.new(e.outputs[0], o.inputs['Surface'])
    return m


M = S['materials']
MAT = {
    # matte under a thin clear coat: the flat faces (values sit on them) light evenly, the bevels catch the light panels
    'panel': principled('panel', M['panel'], rough=0.75, spec=S.get('face_spec', 0.10), Coat_Weight=S.get('coat', 1.0), Coat_Roughness=0.12),
    'cell': principled('cell', M['cell'], rough=0.75, spec=S.get('face_spec', 0.10), Coat_Weight=S.get('coat', 1.0), Coat_Roughness=0.12),
    'header': principled('header', M['header'], rough=0.75, spec=S.get('face_spec', 0.10), Coat_Weight=S.get('coat', 1.0), Coat_Roughness=0.12),
    'pad': principled('pad', M['pad'], rough=0.75, spec=S.get('face_spec', 0.10), Coat_Weight=S.get('coat', 1.0), Coat_Roughness=0.12),
    'pad_me': principled('pad_me', M['pad_me'], rough=0.6, spec=0.2, Coat_Weight=S.get('coat', 1.0), Coat_Roughness=0.12),
    'metal': principled('metal', M['metal'], metallic=1.0, rough=0.30, spec=0.6, aniso=0.65),
    'metal_soft': principled('metal_soft', M['metal'], metallic=1.0, rough=0.38, spec=0.6),
    'anod': principled('anod', M['anod'], metallic=0.9, rough=0.30, spec=0.6, aniso=0.55),
    'dark': principled('dark', (6, 11, 8), rough=0.5, spec=0.3),
    'neon': emissive('neon', M['neon'], S['neon_strength']),
    'neon_soft': emissive('neon_soft', M['neon'], S['neon_strength'] * 0.5),
    'neon_strip': emissive('neon_strip', M['neon'], S.get('strip_strength', 2.2)),
    'floor_glow': emissive('floor_glow', M['neon'], S.get('floor_glow_strength', 3.5)),
    'blade': principled('blade', M['blade'], metallic=0.0, rough=0.7, spec=0.12),
    'well': emissive('well', M['well'], 1.0),
    'badge_face': emissive('badge_face', M['badge'], 1.0),
    'glass': principled('glass', (230, 255, 240), rough=0.04, spec=0.5, Transmission_Weight=1.0, IOR=1.45),
    'chip': principled('chip', M['chip'], rough=0.75, spec=S.get('face_spec', 0.10), Coat_Weight=S.get('coat', 1.0), Coat_Roughness=0.12),
}


# ---------------------------------------------------------------- mesh helpers
def link(ob):
    sc.collection.objects.link(ob)
    return ob


def P(x, y, z=0.0):
    return Vector((x, H - y, z))


def smooth(ob, angle=35):
    bpy.ops.object.select_all(action='DESELECT')
    bpy.context.view_layer.objects.active = ob
    ob.select_set(True)
    bpy.ops.object.shade_smooth_by_angle(angle=math.radians(angle))
    ob.select_set(False)


def apply_modifiers(ob):
    bpy.ops.object.select_all(action='DESELECT')
    bpy.context.view_layer.objects.active = ob
    ob.select_set(True)
    for mod in list(ob.modifiers):
        bpy.ops.object.modifier_apply(modifier=mod.name)
    ob.select_set(False)


def rr_pts(x0, y0, x1, y1, r, seg=8):
    pts = []
    for (cx, cy, a0) in ((x1 - r, y0 + r, -90), (x1 - r, y1 - r, 0), (x0 + r, y1 - r, 90), (x0 + r, y0 + r, 180)):
        for i in range(seg + 1):
            a = math.radians(a0 + 90.0 * i / seg)
            pts.append((cx + r * math.cos(a), cy + r * math.sin(a)))
    return pts


def slab_poly(name, pts, z0, z1, mat, bevel=2.0, segs=3):
    """A convex outline (dash px) extruded from z0 up to z1, the top edges bevelled."""
    bm = bmesh.new()
    vs = [bm.verts.new(P(px, py, z0)) for px, py in pts]
    f = bm.faces.new(vs)
    r = bmesh.ops.extrude_face_region(bm, geom=[f])
    moved = [e for e in r['geom'] if isinstance(e, bmesh.types.BMVert)]
    bmesh.ops.translate(bm, vec=(0, 0, z1 - z0), verts=moved)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    ob = link(bpy.data.objects.new(name, me))
    me.materials.append(mat)
    if bevel:
        mod = ob.modifiers.new('bevel', 'BEVEL')
        mod.width = bevel
        mod.segments = segs
        mod.limit_method = 'ANGLE'
        mod.angle_limit = math.radians(30)
        apply_modifiers(ob)
    smooth(ob)
    return ob


def slab(name, rect, r, z0, z1, mat, bevel=2.0, segs=3):
    x0, y0, x1, y1 = rect
    return slab_poly(name, rr_pts(x0, y0, x1, y1, r), z0, z1, mat, bevel, segs)


def lathe(name, cx, cy, prof, mats, nseg=360):
    """prof: list of (r, z, material index of the strip that ENDS at this point); traversed from the outside in. Each strip
    has its own vertices, so the shading is smooth round the lens and crisp between strips."""
    verts, faces, midx = [], [], []
    for j in range(1, len(prof)):
        (r0, z0, _), (r1, z1, mi) = prof[j - 1], prof[j]
        base = len(verts)
        for i in range(nseg):
            a = 2 * math.pi * i / nseg
            verts.append((cx + r0 * math.cos(a), H - cy + r0 * math.sin(a), z0))
            verts.append((cx + r1 * math.cos(a), H - cy + r1 * math.sin(a), z1))
        for i in range(nseg):
            i2 = (i + 1) % nseg
            a0, b0, a1, b1 = base + 2 * i, base + 2 * i + 1, base + 2 * i2, base + 2 * i2 + 1
            faces.append((a0, a1, b1, b0))
            midx.append(mi)
    me = bpy.data.meshes.new(name)
    me.from_pydata(verts, [], faces)
    me.update()
    for m in mats:
        me.materials.append(m)
    for p, mi in zip(me.polygons, midx):
        p.material_index = mi
        p.use_smooth = True
    return link(bpy.data.objects.new(name, me))


def disc(name, cx, cy, r, z, mat, nseg=256):
    verts = [(cx, H - cy, z)] + [(cx + r * math.cos(2 * math.pi * i / nseg), H - cy + r * math.sin(2 * math.pi * i / nseg), z)
                                 for i in range(nseg)]
    faces = [(0, 1 + i, 1 + (i + 1) % nseg) for i in range(nseg)]
    me = bpy.data.meshes.new(name)
    me.from_pydata(verts, [], faces)
    me.update()
    me.materials.append(mat)
    return link(bpy.data.objects.new(name, me))


def plane(name, rect, z, mat):
    x0, y0, x1, y1 = rect
    verts = [P(x0, y1, z), P(x1, y1, z), P(x1, y0, z), P(x0, y0, z)]
    me = bpy.data.meshes.new(name)
    me.from_pydata(verts, [], [(0, 1, 2, 3)])
    me.uv_layers.new(name='uv')
    uv = me.uv_layers['uv'].data
    for i, (u, v) in enumerate(((0, 0), (1, 0), (1, 1), (0, 1))):
        uv[i].uv = (u, v)
    me.update()
    me.materials.append(mat)
    return link(bpy.data.objects.new(name, me))


def cyl_y(name, cx, y0, y1, r, z, mat, verts=96, bevel=0.0):
    """A cylinder lying along y (dash px y0..y1) at height z."""
    L = abs(y1 - y0)
    bpy.ops.mesh.primitive_cylinder_add(vertices=verts, radius=r, depth=L, location=P(cx, (y0 + y1) / 2, z), rotation=(math.pi / 2, 0, 0))
    ob = bpy.context.active_object
    ob.name = name
    ob.data.materials.append(mat)
    if bevel:
        mod = ob.modifiers.new('bevel', 'BEVEL')
        mod.width = bevel
        mod.segments = 3
        apply_modifiers(ob)
    smooth(ob, 40)
    return ob


# ---------------------------------------------------------------- the scene
# the floor, with the glow and ripples painted into its texture
plane('floor', (0, 0, W, H), 0.0, image_material('floor', S['floor_img'], S['floor_emission'], diffuse=True))

for sl in S['slabs']:
    slab(sl['name'], sl['rect'], sl['r'], sl['z0'], sl['z1'], MAT[sl['mat']], sl.get('bevel', 2.0))
for pg in S.get('polys', []):
    slab_poly(pg['name'], pg['pts'], pg['z0'], pg['z1'], MAT[pg['mat']], pg.get('bevel', 1.5))

# ---- the lens: a machined bezel, a recess with a glowing floor and two counter-rotating turbine stages, an inner rim,
# an edge-lit glass hub for the gear
L = S['lens']
cx, cy = L['cx'], L['cy']
LM = [MAT['metal'], MAT['dark'], MAT['anod'], MAT['metal_soft']]
lathe('bezel', cx, cy, [tuple(p) for p in L['bezel']], LM)
lathe('rim', cx, cy, [tuple(p) for p in L['rim']], LM)
fl = L['floor']
lathe('floor glow', cx, cy, [(fl['r1'], fl['z'], 0), (fl['r0'], fl['z'], 0)], [MAT['floor_glow']], nseg=360)


def blade(name, r0, r1, sweep, width, pitch, thick, zc, taper=0.2, steps=14):
    """One fan blade: a ribbon running out along r while it sweeps round by `sweep` degrees, tilted (pitched) about its own
    length so it shows a face to the camera, then given thickness. Built round the origin; copies are rotated about z."""
    phi = math.radians(pitch)
    verts, faces = [], []
    for i in range(steps + 1):
        t = i / steps
        r = r0 + (r1 - r0) * t
        a = math.radians(sweep) * t
        c = Vector((r * math.cos(a), r * math.sin(a), zc))
        tan = Vector((-math.sin(a), math.cos(a), 0.0))
        o = (tan * math.cos(phi) + Vector((0, 0, 1)) * math.sin(phi)) * (width * (1 - taper * t) / 2)
        verts += [tuple(c - o), tuple(c + o)]
    for i in range(steps):
        faces.append((2 * i, 2 * i + 1, 2 * i + 3, 2 * i + 2))
    me = bpy.data.meshes.new(name)
    me.from_pydata(verts, [], faces)
    me.update()
    me.materials.append(MAT['blade'])
    ob = link(bpy.data.objects.new(name, me))
    mod = ob.modifiers.new('solid', 'SOLIDIFY')
    mod.thickness = thick
    mod.offset = 0
    apply_modifiers(ob)
    smooth(ob, 50)
    return ob


for st in L['turbine']:
    proto = blade(st['name'], st['r0'], st['r1'], st['sweep'], st['width'], st['pitch'], st['thick'], st['z'])
    proto.location = P(cx, cy, 0)
    for k in range(st['n']):
        ob = proto if k == 0 else proto.copy()
        if k:
            link(ob)
        ob.location = P(cx, cy, 0)
        ob.rotation_euler = (0, 0, 2 * math.pi * k / st['n'] + math.radians(st.get('phase', 0)))

# the hub: an edge-lit glass disc, flat under the gear (exactly this colour), brighter toward its rim
Hb = L['hub']
verts = [(0, 0, 0)] + [(Hb['r'] * math.cos(2 * math.pi * i / 256), Hb['r'] * math.sin(2 * math.pi * i / 256), 0) for i in range(256)]
me = bpy.data.meshes.new('hub')
me.from_pydata(verts, [], [(0, 1 + i, 1 + (i + 1) % 256) for i in range(256)])
me.update()
hm = bpy.data.materials.new('hub')
hm.use_nodes = True
hn = hm.node_tree
hn.nodes.clear()
tcn = hn.nodes.new('ShaderNodeTexCoord')
ln = hn.nodes.new('ShaderNodeVectorMath')
ln.operation = 'LENGTH'
mr = hn.nodes.new('ShaderNodeMapRange')
mr.inputs['From Min'].default_value = Hb['r_flat']
mr.inputs['From Max'].default_value = Hb['r']
mr.interpolation_type = 'SMOOTHSTEP'
mx = hn.nodes.new('ShaderNodeMixRGB')
mx.inputs['Color1'].default_value = rgb(Hb['col'])
mx.inputs['Color2'].default_value = rgb(Hb['edge_col'])
em = hn.nodes.new('ShaderNodeEmission')
sm = hn.nodes.new('ShaderNodeMath')
sm.operation = 'MULTIPLY_ADD'
sm.inputs[1].default_value = Hb['edge_boost']
sm.inputs[2].default_value = 1.0
oo = hn.nodes.new('ShaderNodeOutputMaterial')
hn.links.new(tcn.outputs['Object'], ln.inputs[0])
hn.links.new(ln.outputs['Value'], mr.inputs['Value'])
hn.links.new(mr.outputs['Result'], mx.inputs['Fac'])
hn.links.new(mr.outputs['Result'], sm.inputs[0])
hn.links.new(mx.outputs['Color'], em.inputs['Color'])
hn.links.new(sm.outputs['Value'], em.inputs['Strength'])
hn.links.new(em.outputs[0], oo.inputs['Surface'])
me.materials.append(hm)
hub = link(bpy.data.objects.new('hub', me))
hub.location = P(cx, cy, Hb['z'])

# the neon outline of the recess
N_ = L['neon']
bpy.ops.mesh.primitive_torus_add(major_radius=N_['r'], minor_radius=N_['minor'], major_segments=360, minor_segments=16,
                                 location=P(cx, cy, N_['z']))
tor = bpy.context.active_object
tor.name = 'neon ring'
tor.data.materials.append(MAT['neon'])
for a in (45, 135, 225, 315):                                    # screws on the bezel
    ar = math.radians(a)
    sx, sy = cx + L['screw_r'] * math.cos(ar), cy + L['screw_r'] * math.sin(ar)
    bpy.ops.mesh.primitive_cylinder_add(vertices=24, radius=2.6, depth=1.4, location=P(sx, sy, L['screw_z']))
    sc_ = bpy.context.active_object
    sc_.name = 'screw'
    sc_.data.materials.append(MAT['metal_soft'])
    smooth(sc_)

# ---- tick marks inlaid in the bezel (one mesh of quads, facing up)
tv, tf = [], []
for q in S.get('ticks', []):
    b = len(tv)
    vs = [P(px, py, S['tick_z']) for px, py in q]
    n = (vs[1] - vs[0]).cross(vs[2] - vs[0])
    tv += vs
    tf.append((b, b + 1, b + 2, b + 3) if n.z > 0 else (b + 3, b + 2, b + 1, b))
if tv:
    me = bpy.data.meshes.new('ticks')
    me.from_pydata(tv, [], tf)
    me.update()
    me.materials.append(MAT['dark'])
    link(bpy.data.objects.new('ticks', me))

# ---- the delta badge: a neon-edged plate with a flat dark face
B = S['badge']
slab('badge body', B['rect'], B['r'], B['z0'], B['z1'], MAT['neon_strip'], bevel=1.6)
x0, y0, x1, y1 = B['rect']
e = B['edge']
slab('badge face', (x0 + e, y0 + e, x1 - e, y1 - e), max(B['r'] - e, 2), B['z0'], B['z1'] + 0.05, MAT['badge_face'], bevel=0.0)

# ---- glass cylinders and their caps
for T in S['tubes']:
    cyl_y('tube', T['cx'], T['y0'], T['y1'], T['r'], T['z'], MAT['glass'])
    cyl_y('tube core', T['cx'], T['y0'], T['y1'], T['core_r'], T['z'], MAT['well'])
    for (ya, yb) in T['caps']:
        cyl_y('cap', T['cx'], ya, yb, T['r'] + 3, T['z'], MAT['metal_soft'], bevel=1.2)

# ---- the sky strip (race progress)
K = S['sky']
plane('sky', K['rect'], K['z'], image_material('sky', K['img'], 1.0))

# ---- an LED strip under the header
for st in S.get('strips', []):
    slab(st['name'], st['rect'], st['r'], st['z0'], st['z1'], MAT['neon_strip'], bevel=0.0)


# ---------------------------------------------------------------- lights, world, camera
def sun(name, az, el, strength, angle, color=(1, 1, 1)):
    ld = bpy.data.lights.new(name, 'SUN')
    ld.energy = strength
    ld.angle = math.radians(angle)
    ld.color = color
    ob = link(bpy.data.objects.new(name, ld))
    a, e = math.radians(az), math.radians(el)
    d = Vector((math.cos(e) * math.cos(a), math.cos(e) * math.sin(a), math.sin(e)))
    ob.rotation_euler = d.to_track_quat('Z', 'Y').to_euler()


LT = S['light']
sun('key', LT['az'], LT['el'], LT['key'], LT['soft'], (1.0, 0.98, 0.95))
sun('fill', LT['az'] + 180, 55, LT['fill'], 25, (0.7, 1.0, 0.85))


def softbox(name, loc, size, col, strength):
    """A big emissive panel out of the camera's view: lights the scene softly and is what the sloped metal reflects."""
    bpy.ops.mesh.primitive_plane_add(size=1, location=loc)
    ob = bpy.context.active_object
    ob.name = name
    ob.scale = (size[0], size[1], 1)
    toward = (Vector((W / 2, H / 2, 0)) - Vector(loc)).normalized()
    ob.rotation_euler = toward.to_track_quat('Z', 'Y').to_euler()
    ob.data.materials.append(emissive(name, col, strength))
    ob.visible_camera = False
    ob.visible_diffuse = False                      # reflections only: flat faces stay evenly lit by the sun and the dome
    ob.visible_glossy = True
    ob.visible_transmission = True


for sb in LT.get('softboxes', []):
    softbox(sb['name'], sb['loc'], sb['size'], sb['col'], sb['strength'])

world = bpy.data.worlds.new('w')
sc.world = world
world.use_nodes = True
nt = world.node_tree
bg = nt.nodes['Background']
tc = nt.nodes.new('ShaderNodeTexCoord')
sep = nt.nodes.new('ShaderNodeSeparateXYZ')
ramp = nt.nodes.new('ShaderNodeValToRGB')
nt.links.new(tc.outputs['Generated'], sep.inputs[0])
nt.links.new(sep.outputs['Z'], ramp.inputs['Fac'])
ramp.color_ramp.elements[0].position = 0.0
ramp.color_ramp.elements[0].color = (0.004, 0.008, 0.006, 1)
ramp.color_ramp.elements[1].position = 1.0
ramp.color_ramp.elements[1].color = tuple(LT['dome']) + (1,)
nt.links.new(ramp.outputs['Color'], bg.inputs['Color'])
bg.inputs['Strength'].default_value = 1.0

cam = link(bpy.data.objects.new('cam', bpy.data.cameras.new('cam')))
sc.camera = cam
cam.data.type = 'ORTHO'
cam.data.ortho_scale = W
cam.data.sensor_fit = 'HORIZONTAL'
cam.location = (W / 2, H / 2, 900)
cam.data.clip_end = 5000

# ---------------------------------------------------------------- render
sc.render.engine = 'CYCLES'
prefs = bpy.context.preferences.addons['cycles'].preferences
for t in ('OPTIX', 'CUDA'):
    try:
        prefs.compute_device_type = t
        prefs.get_devices()
        devs = [d for d in prefs.devices if d.type == t]
        if devs:
            for d in prefs.devices:
                d.use = d.type == t
            sc.cycles.device = 'GPU'
            break
    except Exception:
        pass
sc.cycles.samples = S.get('samples', 512)
sc.cycles.use_denoising = True
sc.cycles.denoiser = 'OPENIMAGEDENOISE'
sc.cycles.max_bounces = 10
sc.cycles.transmission_bounces = 10
sc.cycles.glossy_bounces = 8
sc.render.resolution_x, sc.render.resolution_y = W * SS, H * SS
sc.render.resolution_percentage = 100
sc.view_settings.view_transform = 'Standard'
sc.view_settings.look = 'None'
sc.render.image_settings.file_format = 'PNG'
sc.render.image_settings.color_mode = 'RGB'
sc.render.filepath = OUT

# glow round the bright parts
sc.use_nodes = True
cn = sc.node_tree
cn.nodes.clear()
rl = cn.nodes.new('CompositorNodeRLayers')
gl = cn.nodes.new('CompositorNodeGlare')
co = cn.nodes.new('CompositorNodeComposite')
gl.glare_type = 'FOG_GLOW'
gl.quality = 'HIGH'
gl.threshold = S.get('glare_threshold', 1.0)
gl.size = S.get('glare_size', 7)
gl.mix = S.get('glare_mix', -0.5)
cn.links.new(rl.outputs['Image'], gl.inputs['Image'])
cn.links.new(gl.outputs['Image'], co.inputs['Image'])

bpy.ops.render.render(write_still=True)
print('rendered', OUT)
