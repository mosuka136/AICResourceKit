"""Validate the authoring schema against shared positive and negative vectors."""
import json
from pathlib import Path

from jsonschema import Draft7Validator

root = Path(__file__).resolve().parents[1] / "docs" / "resource-replacement"
schema = json.loads((root / "resource-replacement.schema.json").read_text(encoding="utf-8"))
Draft7Validator.check_schema(schema)
validator = Draft7Validator(schema)
cases = json.loads((root / "contract-vectors.json").read_text(encoding="utf-8"))["cases"]
failures = []
for case in cases:
    errors = list(validator.iter_errors(case["manifest"]))
    if (not errors) != case["schemaValid"]:
        failures.append((case["name"], [error.message for error in errors]))
if failures:
    raise AssertionError(failures)
print(f"Schema and all {len(cases)} authoring vectors passed.")
