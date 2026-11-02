"""Build-time import-table audit (requires pefile, never shipped to users)."""
import pathlib, sys, json
import pefile
root = pathlib.Path(sys.argv[1]).resolve()
files = [p for p in root.rglob('*') if p.suffix.lower() in ('.dll', '.pyd', '.exe')]
names = {p.name.lower() for p in files}
imports = {}
for p in files:
    try:
        pe = pefile.PE(str(p), fast_load=True)
        pe.parse_data_directories(directories=[1, 13])
        imports[str(p.relative_to(root))] = [i.dll.decode() for kind in ('DIRECTORY_ENTRY_IMPORT', 'DIRECTORY_ENTRY_DELAY_IMPORT') for i in getattr(pe, kind, [])]
        pe.close()
    except pefile.PEFormatError:
        pass
external = sorted({d for ds in imports.values() for d in ds if d.lower() not in names and not d.lower().startswith(('api-ms-', 'ext-ms-'))})
pathlib.Path(sys.argv[2]).write_text(json.dumps(dict(external=external, imports=imports), indent=2), encoding='utf-8')
print('External modules (must be reviewed as OS components):', external)
