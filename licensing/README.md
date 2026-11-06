# Licensing keys

BrightPAS licence files are signed with the vendor's **private key** (ECDSA P-256) and checked with the matching **public key** built into BrightPAS.

| File | What | Where it lives |
|---|---|---|
| `dev/vendor-public-key.pem` | Development/sandbox public key (always trusted; licences signed with it show "DEVELOPMENT KEY") | This repository |
| `dev/vendor-private-key.DEVELOPMENT-ONLY.pem` | Development private key – signs sandbox licences only | This repository (deliberately public to the team) |
| `vendor-public-key.pem` | **Production** public key – add before the first production build | This repository |
| production private key | Signs real customer licences | **Never in the repository.** Encrypted file on an offline USB drive + backup in a safe; password in the TDS password manager. |

Create the production key pair once with `BrightPAS-LicenceManager keygen`, then copy only `vendor-public-key.pem` here and rebuild. A production build can be set to refuse development-key licences (`Licensing:AllowDevelopmentKey=false`).
