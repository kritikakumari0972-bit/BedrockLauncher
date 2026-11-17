# Resumable package downloads

The Windows launcher uses a shared HTTP downloader for UWP Store-resolved packages and exact-identity GDK package URLs.

## Resume rules

- Incomplete bytes are kept in a sibling `.download` file.
- A small sidecar stores a SHA-256 fingerprint of the URL (not the URL itself), the expected length, and the server validator.
- Resume is attempted only when the URL fingerprint matches and the server supplied a strong ETag or a parseable Last-Modified value.
- The resumed request uses HTTP `Range` plus `If-Range`.
- A valid `206 Partial Content` response must begin at the exact local byte offset. If the server returns `200 OK`, the local partial is truncated and the full response is written from byte zero.
- A `416 Range Not Satisfiable`, malformed range, or changed validator causes a clean retry rather than appending potentially mismatched bytes.
- The final package path is replaced only after the transfer completes and its byte count matches the declared total length (when known).

## Integrity boundary

Content length and HTTP validators prevent accidental truncation and unsafe mixing of different server representations. They are not a cryptographic authenticity check. The current version catalogues do not consistently provide trusted SHA-256 digests, so this implementation does not invent or claim one. A future catalogue schema can add a trusted digest and the downloader can verify it before promotion.

## Test coverage

The pipeline tests cover interruption followed by a ranged continuation, and the server returning a full response to a range request (which must restart rather than append). Windows CI still needs to execute these tests and build the full launcher before this milestone can be called verified.
