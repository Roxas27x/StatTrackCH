from __future__ import annotations

import argparse
import ctypes
import re
import shutil
import struct
import sys
from pathlib import Path

import lz4.block
import UnityPy
from UnityPy.streams import EndianBinaryReader


DEFAULT_ASSET_PATH = Path(r"C:\Users\Roxas\Documents\GDBOT\clone-hero-v1-writable\Clone Hero_Data\sharedassets1.assets")
DEFAULT_CLEAN_ASSET_PATH = Path(r"C:\Users\Roxas\Documents\GDBOT\CHCLEANDONOTOVERWRITE\Clone Hero\Clone Hero_Data\sharedassets1.assets")
TEMPLATE_HLSL_PATH = Path(r"C:\Users\Roxas\Documents\GDBOT\temp-menu-patch-test\menu_animated_stock_exact_tinted_violet_blackwisps.hlsl")
D3DCOMPILER_PATH = Path(r"C:\Windows\System32\D3DCompiler_47.dll")

SHADER_PATH_ID = 43
D3D11_PLATFORM = 4
TARGET_PROGRAM_TYPE = 17  # DX11 pixel shader SM4.0

PATCHED_BLOCK = """
    float rawWispStrength = saturate(u_xlat8.x * u_xlat12);
    float defaultWispStrength = saturate(pow(max(rawWispStrength, 0.0), 0.72) * 1.55);
    float sizeControl = saturate(_StatTrackMenuWispSize);
    float customBlend = saturate(_StatTrackMenuWispEnabled);
    float customExponent = lerp(1.45, 0.38, sizeControl);
    float customGain = lerp(0.95, 2.35, sizeControl);
    float customWispStrength = saturate(pow(max(rawWispStrength, 0.0), customExponent) * customGain);
    float boostedWispStrength = lerp(defaultWispStrength, customWispStrength, customBlend);
    float3 customWispColor = saturate(float3(_StatTrackMenuWispColorR, _StatTrackMenuWispColorG, _StatTrackMenuWispColorB));
    float3 wispColor = lerp(float3(1.0, 1.0, 1.0), customWispColor, customBlend);
    float wispColorScale = lerp(1.25, lerp(0.85, 1.85, saturate(_StatTrackMenuWispColorA)), customBlend);
    float3 baseColor = u_xlat1.rgb;
    float3 finalColor = baseColor + boostedWispStrength.xxx * wispColor * wispColorScale;
    float finalAlpha = saturate(u_xlat1.a + boostedWispStrength * lerp(1.15, 1.35, customBlend));
    return float4(saturate(finalColor), finalAlpha);
}
""".strip()

PATCHED_GLOBALS_BLOCK = """
cbuffer Globals : register(b0)
{
    float _StatTrackMenuWispColorR;
    float _StatTrackMenuWispColorG;
    float _StatTrackMenuWispColorB;
    float _StatTrackMenuWispColorA;
    float _StatTrackMenuWispSize;
    float _StatTrackMenuWispEnabled;
    float2 _StatTrackMenuWispPad;
    float4 _Pad2;
    float4 _MainTex_TexelSize;
};
""".strip()

CUSTOM_GLOBAL_FLOATS = [
    ("_StatTrackMenuWispColorR", 0),
    ("_StatTrackMenuWispColorG", 4),
    ("_StatTrackMenuWispColorB", 8),
    ("_StatTrackMenuWispColorA", 12),
    ("_StatTrackMenuWispSize", 16),
    ("_StatTrackMenuWispEnabled", 20),
]


class ID3DBlob(ctypes.Structure):
    pass


ID3DBlob._fields_ = [("lpVtbl", ctypes.POINTER(ctypes.c_void_p))]


def _blob_pointer_method(blob_ptr: ctypes.POINTER(ID3DBlob), index: int, restype, *argtypes):
    vtbl = ctypes.cast(blob_ptr.contents.lpVtbl, ctypes.POINTER(ctypes.c_void_p))
    prototype = ctypes.WINFUNCTYPE(restype, ctypes.POINTER(ID3DBlob), *argtypes)
    return prototype(vtbl[index])


def get_blob_bytes(blob_ptr: ctypes.POINTER(ID3DBlob) | None) -> bytes:
    if not blob_ptr:
        return b""
    get_pointer = _blob_pointer_method(blob_ptr, 3, ctypes.c_void_p)
    get_size = _blob_pointer_method(blob_ptr, 4, ctypes.c_size_t)
    pointer = get_pointer(blob_ptr)
    size = get_size(blob_ptr)
    return ctypes.string_at(pointer, size)


def release_blob(blob_ptr: ctypes.POINTER(ID3DBlob) | None) -> None:
    if not blob_ptr:
        return
    release = _blob_pointer_method(blob_ptr, 2, ctypes.c_uint)
    release(blob_ptr)


def compile_pixel_shader(source_text: str) -> bytes:
    compiler = ctypes.WinDLL(str(D3DCOMPILER_PATH))
    d3d_compile = compiler.D3DCompile
    d3d_compile.argtypes = [
        ctypes.c_void_p,
        ctypes.c_size_t,
        ctypes.c_char_p,
        ctypes.c_void_p,
        ctypes.c_void_p,
        ctypes.c_char_p,
        ctypes.c_char_p,
        ctypes.c_uint,
        ctypes.c_uint,
        ctypes.POINTER(ctypes.POINTER(ID3DBlob)),
        ctypes.POINTER(ctypes.POINTER(ID3DBlob)),
    ]
    d3d_compile.restype = ctypes.c_long

    source_bytes = source_text.encode("utf-8")
    code_blob = ctypes.POINTER(ID3DBlob)()
    error_blob = ctypes.POINTER(ID3DBlob)()
    result = d3d_compile(
        ctypes.c_char_p(source_bytes),
        len(source_bytes),
        b"animated_menu_tint.hlsl",
        None,
        None,
        b"main",
        b"ps_4_0",
        0,
        0,
        ctypes.byref(code_blob),
        ctypes.byref(error_blob),
    )

    try:
        if result != 0:
            error_text = get_blob_bytes(error_blob).decode("utf-8", errors="replace")
            raise RuntimeError(f"D3DCompile failed: 0x{result & 0xFFFFFFFF:08X}\n{error_text}")
        return get_blob_bytes(code_blob)
    finally:
        release_blob(code_blob)
        release_blob(error_blob)


def build_patched_hlsl() -> str:
    template = TEMPLATE_HLSL_PATH.read_text(encoding="utf-8")
    globals_pattern = re.compile(
        r"cbuffer Globals : register\(b0\)\s*\{\s*float4 _Pad0;\s*float4 _Pad1;\s*float4 _Pad2;\s*float4 _MainTex_TexelSize;\s*\};",
        re.DOTALL,
    )
    template, globals_replacements = globals_pattern.subn(PATCHED_GLOBALS_BLOCK, template, count=1)
    if globals_replacements != 1:
        raise RuntimeError("Could not find the Globals cbuffer in the animated menu shader template.")
    pattern = re.compile(
        r"float wispStrength = u_xlat8\.x \* u_xlat12;.*?return float4\(saturate\(finalColor\), finalAlpha\);\s*\}",
        re.DOTALL,
    )
    patched, replacements = pattern.subn(PATCHED_BLOCK, template)
    if replacements != 1:
        raise RuntimeError("Could not find the final color block in the animated menu shader template.")
    return patched


def entry_value(entry):
    return entry[0] if isinstance(entry, list) else entry


def read_shader_subprogram(data: bytes) -> dict[str, object]:
    reader = EndianBinaryReader(data, endian="<")
    version = reader.read_int()
    program_type = reader.read_int()
    unknown_12 = bytes(reader.read_bytes(12))
    unknown_4 = b""
    if version >= 201608170:
        unknown_4 = bytes(reader.read_bytes(4))
    keywords_start = reader.Position
    keyword_count = reader.read_int()
    for _ in range(keyword_count):
        reader.read_aligned_string()
    local_keywords = None
    if 201806140 <= version < 202012090:
        local_keywords_start = reader.Position
        local_keyword_count = reader.read_int()
        for _ in range(local_keyword_count):
            reader.read_aligned_string()
        local_keywords = data[local_keywords_start:reader.Position]
    keyword_block = data[keywords_start:reader.Position]
    code_length_position = reader.Position
    code_length = reader.read_int()
    code_position = reader.Position
    code = bytes(reader.read_bytes(code_length))
    reader.align_stream()
    aligned_end = reader.Position
    suffix = data[aligned_end:]
    return {
        "version": version,
        "program_type": program_type,
        "unknown_12": unknown_12,
        "unknown_4": unknown_4,
        "keyword_block": keyword_block,
        "code_length_position": code_length_position,
        "code": code,
        "suffix": suffix,
    }


def serialize_shader_subprogram(info: dict[str, object], new_code: bytes | None = None) -> bytes:
    version = int(info["version"])
    program_type = int(info["program_type"])
    code = new_code if new_code is not None else bytes(info["code"])
    payload = bytearray()
    payload.extend(struct.pack("<I", version))
    payload.extend(struct.pack("<I", program_type))
    payload.extend(bytes(info["unknown_12"]))
    if version >= 201608170:
        payload.extend(bytes(info["unknown_4"]))
    payload.extend(bytes(info["keyword_block"]))
    payload.extend(struct.pack("<I", len(code)))
    payload.extend(code)
    while len(payload) % 4 != 0:
        payload.append(0)
    payload.extend(bytes(info["suffix"]))
    return bytes(payload)


def patch_d3d11_chunk(shader, new_pixel_shader: bytes) -> None:
    blob = bytes(shader.compressedBlob)
    chunk_index = next(
        index for index, platform in enumerate(shader.platforms) if entry_value(platform) == D3D11_PLATFORM
    )
    chunk_offset = entry_value(shader.offsets[chunk_index])
    chunk_compressed_length = entry_value(shader.compressedLengths[chunk_index])
    chunk_decompressed_length = entry_value(shader.decompressedLengths[chunk_index])

    chunk = blob[chunk_offset : chunk_offset + chunk_compressed_length]
    decompressed = lz4.block.decompress(chunk, uncompressed_size=chunk_decompressed_length)

    subprogram_count = struct.unpack_from("<I", decompressed, 0)[0]
    entries = [struct.unpack_from("<III", decompressed, 4 + index * 12) for index in range(subprogram_count)]
    subprogram_bytes = []
    patched = False
    for offset, length, unknown in entries:
        raw = decompressed[offset : offset + length]
        info = read_shader_subprogram(raw)
        if int(info["program_type"]) == TARGET_PROGRAM_TYPE:
            raw = serialize_shader_subprogram(info, new_pixel_shader)
            patched = True
        subprogram_bytes.append((raw, unknown))

    if not patched:
        raise RuntimeError("Did not find the DX11 pixel shader program inside the D3D11 chunk.")

    new_chunk = bytearray()
    new_chunk.extend(struct.pack("<I", subprogram_count))
    header_size = 4 + subprogram_count * 12
    current_offset = header_size
    serialized_entries = []
    for raw, unknown in subprogram_bytes:
        serialized_entries.append((current_offset, len(raw), unknown))
        current_offset += len(raw)

    for offset, length, unknown in serialized_entries:
        new_chunk.extend(struct.pack("<III", offset, length, unknown))
    for raw, _ in subprogram_bytes:
        new_chunk.extend(raw)

    compressed_chunk = lz4.block.compress(bytes(new_chunk), store_size=False)

    new_blob = bytearray()
    new_offsets = []
    new_compressed_lengths = []
    cursor = 0
    for index, platform in enumerate(shader.platforms):
        offset = entry_value(shader.offsets[index])
        length = entry_value(shader.compressedLengths[index])
        if index == chunk_index:
            part = compressed_chunk
            decompressed_length = len(new_chunk)
        else:
            part = blob[offset : offset + length]
            decompressed_length = entry_value(shader.decompressedLengths[index])
        new_offsets.append([cursor] if isinstance(shader.offsets[index], list) else cursor)
        new_compressed_lengths.append([len(part)] if isinstance(shader.compressedLengths[index], list) else len(part))
        if isinstance(shader.decompressedLengths[index], list):
            shader.decompressedLengths[index] = [decompressed_length]
        else:
            shader.decompressedLengths[index] = decompressed_length
        new_blob.extend(part)
        cursor += len(part)

    shader.offsets = new_offsets
    shader.compressedLengths = new_compressed_lengths
    shader.compressedBlob = list(new_blob)


def patch_shader_metadata(shader) -> None:
    parsed_form = shader.m_ParsedForm
    if not parsed_form.m_SubShaders:
        raise RuntimeError("Animated menu shader has no subshaders.")

    shader_pass = parsed_form.m_SubShaders[0].m_Passes[0]
    name_indices = list(shader_pass.m_NameIndices)
    existing_name_map = {name: index for name, index in name_indices}
    next_name_index = (max(existing_name_map.values()) + 1) if existing_name_map else 0

    def ensure_name_index(name: str) -> int:
        nonlocal next_name_index
        if name in existing_name_map:
            return existing_name_map[name]
        existing_name_map[name] = next_name_index
        name_indices.append((name, next_name_index))
        next_name_index += 1
        return existing_name_map[name]

    for name, _ in CUSTOM_GLOBAL_FLOATS:
        ensure_name_index(name)

    shader_pass.m_NameIndices = name_indices

    fragment_common = shader_pass.progFragment.m_CommonParameters
    if not fragment_common.m_ConstantBuffers:
        raise RuntimeError("Animated menu shader fragment program has no constant buffers.")

    globals_buffer = fragment_common.m_ConstantBuffers[0]
    vector_param_type = type(globals_buffer.m_VectorParams[0]) if globals_buffer.m_VectorParams else None
    if vector_param_type is None:
        raise RuntimeError("Animated menu shader globals buffer is missing vector parameter metadata.")

    # Rebuild a clean ordered parameter list so offsets stay deterministic.
    rebuilt_params = []
    for param in globals_buffer.m_VectorParams:
        param_name = next((name for name, idx in shader_pass.m_NameIndices if idx == param.m_NameIndex), None)
        if param_name == "_MainTex_TexelSize":
            rebuilt_params.append(param)

    main_tex_param = next((param for param in rebuilt_params if next((name for name, idx in shader_pass.m_NameIndices if idx == param.m_NameIndex), None) == "_MainTex_TexelSize"), None)
    if main_tex_param is None:
        raise RuntimeError("Animated menu shader metadata is missing _MainTex_TexelSize.")

    custom_params = [
        vector_param_type(m_ArraySize=0, m_Dim=1, m_Index=offset, m_NameIndex=existing_name_map[name], m_Type=0)
        for name, offset in CUSTOM_GLOBAL_FLOATS
    ]
    globals_buffer.m_VectorParams = custom_params + [main_tex_param]


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Patch Clone Hero's animated menu shader to boost stock wisps.")
    parser.add_argument("asset_path", nargs="?", default=str(DEFAULT_ASSET_PATH))
    parser.add_argument("clean_asset_path", nargs="?", default=str(DEFAULT_CLEAN_ASSET_PATH))
    return parser.parse_args()


def main() -> int:
    args = parse_args()
    asset_path = Path(args.asset_path).resolve()
    clean_asset_path = Path(args.clean_asset_path).resolve()
    backup_path = asset_path.with_name("sharedassets1.assets.animatedmenu-runtime-tint.bak")

    if not asset_path.exists():
        raise FileNotFoundError(f"Writable asset not found: {asset_path}")
    if not clean_asset_path.exists():
        raise FileNotFoundError(f"Clean asset not found: {clean_asset_path}")
    if not TEMPLATE_HLSL_PATH.exists():
        raise FileNotFoundError(f"Shader template not found: {TEMPLATE_HLSL_PATH}")
    if not D3DCOMPILER_PATH.exists():
        raise FileNotFoundError(f"D3D compiler DLL not found: {D3DCOMPILER_PATH}")

    if not backup_path.exists():
        shutil.copy2(asset_path, backup_path)

    shutil.copy2(clean_asset_path, asset_path)

    patched_hlsl = build_patched_hlsl()
    dxbc = compile_pixel_shader(patched_hlsl)

    env = UnityPy.load(str(asset_path))
    shader_obj = next((item for item in env.objects if item.path_id == SHADER_PATH_ID), None)
    if shader_obj is None:
        raise RuntimeError(f"Shader path id {SHADER_PATH_ID} not found in {asset_path}.")
    shader = shader_obj.read()
    patch_d3d11_chunk(shader, dxbc)
    patch_shader_metadata(shader)
    shader_obj.save_typetree(shader)

    asset_path.write_bytes(env.file.save())

    print(f"Patched {asset_path}")
    print(f"Backup: {backup_path}")
    print(f"DXBC size: {len(dxbc)} bytes")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
