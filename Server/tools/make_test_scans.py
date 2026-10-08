"""변환 파이프라인 시험용 스캔 파일을 만듭니다 (Blender 커맨드라인 스크립트).

    blender -b --factory-startup -P tools/make_test_scans.py -- <출력 폴더>

만드는 파일
    heavy.glb        UV 구 약 5만 삼각형, 2048px 텍스처, 지름 1.2m, 원점에서 (0.3, 0.2, 0) 벗어난 위치
    multi.glb        재질·텍스처 2개짜리 상자 (텍스처 1장으로 굽기 확인)
    capture.usdz     USDZ (Object Capture 결과 형식) — 텍스처 있는 원기둥, 높이 0.3m
    broken.glb       깨진 파일 (변환 실패 → 등록 안 됨 확인)
"""

import os
import sys

import bpy


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def checker_image(name, size, color_a, color_b):
    image = bpy.data.images.new(name, size, size)
    pixels = []
    cell = max(1, size // 16)
    for y in range(size):
        for x in range(size):
            c = color_a if ((x // cell) + (y // cell)) % 2 == 0 else color_b
            pixels.extend((c[0], c[1], c[2], 1.0))
    image.pixels = pixels
    image.pack()
    return image


def textured_material(name, image):
    mat = bpy.data.materials.new(name)
    if mat.node_tree is None:
        mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    tex = mat.node_tree.nodes.new("ShaderNodeTexImage")
    tex.image = image
    mat.node_tree.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    return mat


def export_selected_glb(path):
    bpy.ops.export_scene.gltf(filepath=path, export_format="GLB", use_selection=True)


def make_heavy(path):
    reset()
    bpy.ops.mesh.primitive_uv_sphere_add(segments=160, ring_count=160, radius=0.6, location=(0.3, 0.2, 0))
    obj = bpy.context.active_object
    obj.data.materials.append(textured_material("Heavy", checker_image("HeavyTex", 2048, (0.9, 0.3, 0.2), (1, 0.9, 0.6))))
    export_selected_glb(path)


def make_multi(path):
    reset()
    bpy.ops.mesh.primitive_cube_add(size=0.2)
    obj = bpy.context.active_object
    obj.data.materials.append(textured_material("A", checker_image("TexA", 256, (0.2, 0.4, 0.9), (1, 1, 1))))
    obj.data.materials.append(textured_material("B", checker_image("TexB", 256, (0.2, 0.8, 0.3), (0, 0, 0))))
    for i, poly in enumerate(obj.data.polygons):
        poly.material_index = i % 2
    export_selected_glb(path)


def make_usdz(path):
    reset()
    bpy.ops.mesh.primitive_cylinder_add(radius=0.08, depth=0.3, vertices=48)
    obj = bpy.context.active_object
    obj.data.materials.append(textured_material("Capture", checker_image("CaptureTex", 512, (0.6, 0.3, 0.8), (1, 1, 0.4))))
    bpy.ops.wm.usd_export(filepath=path, selected_objects_only=True)


def main(out_dir):
    os.makedirs(out_dir, exist_ok=True)
    make_heavy(os.path.join(out_dir, "heavy.glb"))
    make_multi(os.path.join(out_dir, "multi.glb"))
    make_usdz(os.path.join(out_dir, "capture.usdz"))
    with open(os.path.join(out_dir, "broken.glb"), "wb") as f:
        f.write(b"glTF" + b"\x00" * 64 + b"not a real gltf file")
    print("test scans ->", out_dir)


if __name__ == "__main__":
    main(sys.argv[sys.argv.index("--") + 1])
