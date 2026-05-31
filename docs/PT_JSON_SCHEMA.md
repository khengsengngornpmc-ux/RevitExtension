# PT JSON Schema

This document defines the custom internal JSON format recommended for `DRAWING PT`.

Important note:

- this is `not` described as an official ADAPT-Builder export format
- this is our `custom intermediate format`
- its purpose is to normalize both project methods into one shared data model
- ADAPT-Builder does `not` need to export JSON for this project to work
- the user-facing workflow still stays on only two methods:
  - direct ADAPT-Builder source import
  - DWG / DXF import

The two project methods are:

1. `Direct ADAPT-Builder import`
2. `DWG / DXF import`

## Why This JSON Exists

The import architecture should converge both methods into one internal representation before Revit creation logic runs.

That gives us:

- one tendon data contract
- one Revit creation pipeline
- one place for future validation
- one place for future takeoff and shop-drawing tooling

This means JSON is an `internal implementation detail`, not a user requirement.

## Schema Name

Current schema identifier:

- `mhnk.pt.import/1.0`

## Top-Level Shape

```json
{
  "schema": "mhnk.pt.import/1.0",
  "importMethod": "direct-adapt",
  "source": {},
  "units": {},
  "tendons": [],
  "cadReferences": [],
  "notes": []
}
```

## Key Fields

### `importMethod`

Expected values:

- `direct-adapt`
- `cad-dwg-dxf`

### `source`

Describes where the tendon data came from.

Typical fields:

- `sourceType`
- `sourcePath`
- `displayName`
- `generator`
- `projectName`

Suggested `sourceType` values:

- `adapt-adm`
- `adapt-table`
- `adapt-dwg`
- `adapt-dxf`
- `converted-json`

### `units`

Defines stored geometry and display units.

Typical fields:

- `geometry`
- `displayLength`
- `station`
- `elevation`

Current recommended stored geometry unit:

- `ft`

That matches the current Revit-side geometry pipeline.

### `tendons`

Each tendon item should represent one tendon or tendon group that can become native Revit geometry and profile output.

Recommended tendon fields:

- `tendonId`
- `groupKey`
- `profileName`
- `tendonName`
- `sourceLabel`
- `sourceLayer`
- `tendonType`
- `strandCount`
- `ductDiameterMm`
- `renderedDiameterMm`
- `centerlinePoints`
- `profileHints`
- `tags`

### `centerlinePoints`

This is the most important geometry field.

Instead of storing only segments, the JSON stores ordered path points:

```json
{
  "xFt": 0.0,
  "yFt": 0.0,
  "zFt": 0.0,
  "stationFt": 0.0,
  "elevationFt": 0.0
}
```

Why points instead of only segments:

- easier to reconstruct segments
- better for drafting/profile generation
- better for metadata such as high/low points
- easier to compare direct import and CAD-derived geometry

### `profileHints`

Optional derived hints used for profile documentation:

- `lowPointStationFt`
- `lowPointElevationFt`
- `highPointStationFt`
- `highPointElevationFt`
- `totalLengthFt`

## How Each Method Uses The JSON

### 1. Direct ADAPT-Builder Import

Flow:

- `.adm` or tendon table
- parser reads source geometry
- parser converts source to `PtImportJsonDocument`
- Revit creation pipeline consumes JSON-backed tendon data

Suggested values:

- `importMethod = "direct-adapt"`
- `source.sourceType = "adapt-adm"` or `adapt-table`

### 2. DWG / DXF Import

Flow:

- `.dwg` / `.dxf`
- CAD import or CAD analysis path identifies tendon-related geometry
- extracted tendon paths are normalized into `PtImportJsonDocument`
- same Revit creation pipeline consumes JSON-backed tendon data

Suggested values:

- `importMethod = "cad-dwg-dxf"`
- `source.sourceType = "adapt-dwg"` or `adapt-dxf`

## Design Principle

The JSON format should support both:

- `geometry creation`
- `workflow automation`

That means it should not stop at line coordinates.

It should gradually include:

- tendon identity
- grouping
- strand / duct information
- CAD reference context
- profile hints
- future shop-drawing metadata

## Relationship To Current Code

The current code already has:

- `AdaptTendonProfileSegmentPayload`

That is a useful low-level runtime payload, but it is still segment-oriented and narrow.

The new JSON model is intended to sit one level higher:

- better for import normalization
- better for future persistence
- better for future automation around PT drafting and checking

## Current Code Models

The repo now includes lightweight C# classes for this schema in:

- `PtJsonModels.cs`

Those classes are a starting contract, not the final feature-complete PT model.

## Example

See the example payload here:

- `docs/PT_JSON_SCHEMA_EXAMPLE.json`
