# Runtime Review Slice Sample Data

These fixtures are derived from real HELPER-oriented runtime scenarios and then sanitized for public release.

Rules:

1. no private paths
2. no operator identity
3. no API keys or tokens
4. no private URLs
5. no private-core scripts

Allowed placeholders:

1. `C:/REDACTED_RUNTIME/...`
2. `USER_REDACTED`
3. `TOKEN_REDACTED`
4. `HOST_REDACTED`

The slice is intentionally fixture-backed. These files are meant to prove boundary shape, runtime review semantics, and reproducible UI behavior without exposing the private core.

Test assumption:

The canonical public test path in `scripts/test.ps1` uses this checked-in `sample_data/` tree directly. Replacing these fixtures changes the public proof surface and is outside the default Stage 1 verification contract.
