# FIX: Add Missing Using Statements to ClientHandler.cs

The file VPN.Server\ClientHandler.cs is missing these using statements:

```csharp
using System.Text;
using System.Text.Json;
```

## MANUAL FIX REQUIRED:

1. Open `VPN.Server\ClientHandler.cs`
2. Add these two lines after line 6 (after `using System.Threading.Tasks;`):

```csharp
using System.Text;
using System.Text.Json;
```

The top of the file should look like:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;              // ? ADD THIS
using System.Text.Json;         // ? ADD THIS
using System.Threading;
using System.Threading.Tasks;
using VPN.Core.Enums;
using VPN.Core.Exceptions;
using VPN.Core.Models;
using VPN.Core.Protocol;
using VPN.Core.Security;
```

3. Save the file
4. Build again - it should succeed!

## Alternative: Use Find & Replace

1. Open ClientHandler.cs
2. Find: `using System.Threading.Tasks;`
3. Replace with:
```
using System.Threading.Tasks;
using System.Text;
using System.Text.Json;
```
4. Save and build
