"""스캔 파일을 Quest용 GLB로 변환·경량화합니다 (Blender 커맨드라인 스크립트).

    blender -b --factory-startup -P tools/convert_scan.py -- <입력> <출력.glb> <보고서.json>

규약(Contract/CONTRACT.md) 경량화 기준
    삼각형 10,000개 이하 (Decimate), 텍스처 1장 1024×1024 이하, GLB 5MB 이하,
    단위 m, 피벗 = 바운딩박스 중심, 최대 길이 0.5m 초과 시 축소

입력 형식: .glb .gltf .obj .fbx .usd .usdz .usdc .usda (Blender 4.2 LTS 이상 권장, 5.2 LTS에서 확인).
보고서 JSON: {"ok": bool, "error": str|null, "triangles": int, "textureSize": [w, h]|null, "dimensions": [x, y, z], "bytes": int}
Blender 내장 Python에서 실행되므로 bpy 외의 외부 패키지를 쓰지 않습니다.
"""

import json
import math
import os
import sys
import traceback

import bmesh
import bpy
from mathutils import Vector

MAX_TRIANGLES = 10_000
DECIMATE_TARGET = 9_500          # 반올림 오차를 감안해 기준보다 조금 낮게
MAX_TEXTURE = 1024
MAX_BYTES = 5 * 1024 * 1024
MAX_LENGTH = 0.5


def main(argv):
    src, dst, report_path = argv
    report = {"ok": False, "error": None, "triangles": 0, "textureSize": None, "dimensions": [0, 0, 0], "bytes": 0,
              "blender": bpy.app.version_string}
    try:
        clear_scene()
        import_file(src)
        obj = join_meshes()
        triangulate(obj)
        if count_triangles(obj) > MAX_TRIANGLES:
            decimate(obj, DECIMATE_TARGET)
        report["triangles"] = count_triangles(obj)
        if report["triangles"] > MAX_TRIANGLES:
            raise RuntimeError(f"감량 후에도 삼각형 {report['triangles']}개 (기준 {MAX_TRIANGLES})")
        report["textureSize"] = single_texture(obj)
        report["dimensions"] = normalize_transform(obj)
        export_glb(obj, dst)
        report["bytes"] = os.path.getsize(dst)
        if report["bytes"] > MAX_BYTES:
            raise RuntimeError(f"GLB {report['bytes'] / 1048576:.1f}MB (기준 5MB)")
        report["ok"] = True
    except Exception as e:  # noqa: BLE001 - 어떤 실패든 보고서로 남기고 등록하지 않음
        report["error"] = f"{type(e).__name__}: {e}"
        traceback.print_exc()
    with open(report_path, "w", encoding="utf-8") as f:
        json.dump(report, f, ensure_ascii=False)


def clear_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def import_file(path):
    ext = os.path.splitext(path)[1].lower()
    if ext in (".glb", ".gltf"):
        bpy.ops.import_scene.gltf(filepath=path)
    elif ext == ".obj":
        if hasattr(bpy.ops.wm, "obj_import"):
            bpy.ops.wm.obj_import(filepath=path)        # Blender 3.2+
        else:
            bpy.ops.import_scene.obj(filepath=path)
    elif ext == ".fbx":
        bpy.ops.import_scene.fbx(filepath=path)
    elif ext in (".usd", ".usdz", ".usdc", ".usda"):
        if not hasattr(bpy.ops.wm, "usd_import"):
            raise RuntimeError(f"이 Blender({bpy.app.version_string})는 USD를 가져올 수 없습니다. Blender 4.2 LTS 이상이 필요합니다.")
        bpy.ops.wm.usd_import(filepath=path)
    else:
        raise RuntimeError(f"지원하지 않는 형식: {ext}")


def join_meshes():
    meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
    if not meshes:
        raise RuntimeError("메쉬가 없습니다")
    bpy.ops.object.select_all(action="DESELECT")
    for o in meshes:
        o.select_set(True)
    bpy.context.view_layer.objects.active = meshes[0]
    # 부모·애니메이션 없이 월드 배치를 정점에 굽습니다.
    bpy.ops.object.parent_clear(type="CLEAR_KEEP_TRANSFORM")
    bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
    if len(meshes) > 1:
        bpy.ops.object.join()
    obj = bpy.context.view_layer.objects.active
    for o in list(bpy.context.scene.objects):
        if o != obj:
            bpy.data.objects.remove(o, do_unlink=True)
    return obj


def triangulate(obj):
    bm = bmesh.new()
    bm.from_mesh(obj.data)
    bmesh.ops.triangulate(bm, faces=bm.faces[:])
    bm.to_mesh(obj.data)
    bm.free()


def count_triangles(obj):
    return sum(len(p.vertices) - 2 for p in obj.data.polygons)


def decimate(obj, target):
    for _ in range(3):  # Collapse 결과가 목표보다 조금 많을 수 있어 최대 3번 반복
        tris = count_triangles(obj)
        if tris <= MAX_TRIANGLES:
            return
        mod = obj.modifiers.new("Decimate", "DECIMATE")
        mod.decimate_type = "COLLAPSE"
        mod.ratio = max(0.01, target / tris)
        mod.use_collapse_triangulate = True
        bpy.context.view_layer.objects.active = obj
        bpy.ops.object.modifier_apply(modifier=mod.name)
        triangulate(obj)


def texture_images(obj):
    images = []
    for slot in obj.material_slots:
        mat = slot.material
        if mat is None or mat.node_tree is None:   # Blender 5부터 재질은 항상 노드를 씀
            continue
        for node in mat.node_tree.nodes:
            if node.type == "TEX_IMAGE" and node.image is not None and node.image not in images:
                images.append(node.image)
    return images


def single_texture(obj):
    """텍스처를 1장 이하, 1024px 이하로 맞춥니다. 2장 이상이면 하나의 텍스처로 굽습니다."""
    images = texture_images(obj)
    if not images:
        return None
    if len(images) > 1 or len([s for s in obj.material_slots if s.material]) > 1:
        image = bake_to_single_texture(obj)
    else:
        image = images[0]
    w, h = image.size
    if max(w, h) > MAX_TEXTURE:
        scale = MAX_TEXTURE / max(w, h)
        image.scale(max(1, int(w * scale)), max(1, int(h * scale)))
    return list(image.size)


def bake_to_single_texture(obj):
    """여러 재질·텍스처를 새 UV 하나에 1024 텍스처 한 장으로 굽고 재질을 하나로 바꿉니다 (Cycles 디퓨즈 색 굽기)."""
    scene = bpy.context.scene
    scene.render.engine = "CYCLES"
    scene.cycles.samples = 1
    scene.cycles.device = "CPU"

    # 편집 모드를 오가면 메쉬 데이터 참조가 무효가 되므로 이름으로만 다룹니다.
    original_uv = obj.data.uv_layers[0].name if obj.data.uv_layers else None
    bake_uv = obj.data.uv_layers.new(name="BakeUV").name
    obj.data.uv_layers.active = obj.data.uv_layers[bake_uv]
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.uv.smart_project(island_margin=0.003)
    bpy.ops.object.mode_set(mode="OBJECT")

    image = bpy.data.images.new("ScanBaked", MAX_TEXTURE, MAX_TEXTURE)
    for slot in obj.material_slots:
        mat = slot.material
        if mat is None:
            continue
        if mat.node_tree is None:
            mat.use_nodes = True                    # Blender 4.x 이하
        node = mat.node_tree.nodes.new("ShaderNodeTexImage")
        node.image = image
        mat.node_tree.nodes.active = node   # 굽기 대상
        # 원래 텍스처는 처음 UV로 읽어야 하므로 UV 노드가 없으면 첫 UV를 명시합니다.
        for tex in [n for n in mat.node_tree.nodes if n.type == "TEX_IMAGE" and n is not node]:
            if not tex.inputs["Vector"].is_linked and original_uv:
                uv = mat.node_tree.nodes.new("ShaderNodeUVMap")
                uv.uv_map = original_uv
                mat.node_tree.links.new(uv.outputs["UV"], tex.inputs["Vector"])

    bpy.ops.object.bake(type="DIFFUSE", pass_filter={"COLOR"}, margin=4, use_clear=True)

    # 구운 텍스처 하나를 쓰는 새 재질로 바꾸고, 처음 UV는 지웁니다.
    baked = bpy.data.materials.new("ScanBaked")
    if baked.node_tree is None:
        baked.use_nodes = True
    bsdf = baked.node_tree.nodes.get("Principled BSDF")
    tex = baked.node_tree.nodes.new("ShaderNodeTexImage")
    tex.image = image
    baked.node_tree.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    obj.data.materials.clear()
    obj.data.materials.append(baked)
    for name in [l.name for l in obj.data.uv_layers if l.name != bake_uv]:
        obj.data.uv_layers.remove(obj.data.uv_layers[name])
    image.pack()
    return image


def normalize_transform(obj):
    """피벗을 바운딩박스 중심으로 옮기고, 가장 긴 변이 0.5m를 넘으면 줄입니다. 결과 크기(m)를 돌려줍니다."""
    corners = [Vector(c) for c in obj.bound_box]
    lo = Vector((min(c.x for c in corners), min(c.y for c in corners), min(c.z for c in corners)))
    hi = Vector((max(c.x for c in corners), max(c.y for c in corners), max(c.z for c in corners)))
    center = (lo + hi) / 2
    size = hi - lo
    longest = max(size)
    if not math.isfinite(longest) or longest <= 0:
        raise RuntimeError("크기가 0인 메쉬")
    scale = MAX_LENGTH / longest if longest > MAX_LENGTH else 1.0
    for v in obj.data.vertices:
        v.co = (v.co - center) * scale
    obj.location = (0, 0, 0)
    obj.rotation_euler = (0, 0, 0)
    obj.scale = (1, 1, 1)
    obj.data.update()
    return [round(size.x * scale, 4), round(size.y * scale, 4), round(size.z * scale, 4)]


def export_glb(obj, path):
    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    kwargs = dict(filepath=path, export_format="GLB", use_selection=True, export_apply=True,
                  export_animations=False, export_yup=True)
    if "export_image_format" in bpy.ops.export_scene.gltf.get_rna_type().properties:
        kwargs["export_image_format"] = "JPEG"   # 사진 스캔 텍스처는 JPEG로 충분하고 GLB가 작아짐
    bpy.ops.export_scene.gltf(**kwargs)


if __name__ == "__main__":
    args = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
    if len(args) != 3:
        print("사용법: blender -b --factory-startup -P convert_scan.py -- <입력> <출력.glb> <보고서.json>")
        sys.exit(2)
    main(args)
