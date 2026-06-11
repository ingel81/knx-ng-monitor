# Third-Party Notices

KNX-NG-Monitor is distributed under the MIT license (see `README.md`). The
**MIT license applies to this project's own source code only.** The binaries,
Docker image and source distribution also bundle third-party components that
remain under their own licenses, listed below. Of these, the KNX Falcon SDK is
**proprietary (not open source)** and deserves particular attention.

## KNX Falcon SDK — proprietary

- **Package:** `Knx.Falcon.Sdk` (and `Knx.Falcon`, `Knx.Falcon.ApplicationData`)
- **Author / copyright:** © 2012–2024 KNX Association cvba, Brussels, Belgium
- **License:** *KNX Tools Software License Agreement* (proprietary EULA), see
  <https://support.knx.org/hc/en-us/articles/360002909959>
- **Cost:** free of license fees.

Key terms relevant to this distribution (paraphrased — the linked agreement is
authoritative):

- Redistribution of the Falcon Software is permitted **only to end-users of the
  software product created with it** — which is exactly how it is shipped here
  (bundled inside the KNX-NG-Monitor binary / Docker image).
- **No reverse engineering** of the Falcon Software.
- The name, logo and trademark of the **KNX Association** and "Falcon" may **not**
  be used without the KNX Association's written permission. ⚠️ *"KNX" is a
  registered trademark of the KNX Association; the project name and branding use
  it at the maintainer's own risk and imply no endorsement or affiliation.*
- The Falcon Software is provided **without warranty**; KNX Association's
  liability is disclaimed.
- The MIT license of this project does **not** extend to the Falcon SDK.

## Other bundled components

| Component | Where | License |
|---|---|---|
| SharpZipLib | Backend (AES ZIP / keyring) | MIT |
| BCrypt.Net-Next | Backend (password hashing) | MIT (OSI) |
| Entity Framework Core / ASP.NET Core / .NET runtime | Backend | MIT |
| System.IdentityModel.Tokens.Jwt | Backend (JWT) | MIT |
| Serilog | Backend (logging) | Apache-2.0 |
| Angular, Angular Material, Angular CDK | Frontend | MIT |
| RxJS | Frontend | Apache-2.0 |
| @microsoft/signalr | Frontend (realtime) | MIT |

## Test fixtures

- `docs/samples/xknxproject/` — public ETS test projects mirrored from
  [XKNX/xknxproject](https://github.com/XKNX/xknxproject), MIT licensed.
- `docs/samples/own/`, `docs/samples/other/` — private fixtures, **not** part of
  the distribution (git-ignored).

---

*This file is a good-faith summary, not legal advice. For redistribution beyond
the scope above, consult each component's full license.*
