# Allow2csharp

[![Join the chat at https://gitter.im/Allow2/Allow2csharp](https://badges.gitter.im/Allow2/Allow2csharp.svg)](https://gitter.im/Allow2/Allow2csharp?utm_source=badge&utm_medium=badge&utm_campaign=pr-badge&utm_content=badge)

todo: Port https://github.com/Allow2/Allow2node to a C# library
(Note: there is a prototype full offline C library we should consider after the base port. Just keep a note here to do so.

Oh, and we should build this library to be easy to install via a package manager if applicable/appropriate.

refer to https://github.com/Allow2/Allow2.github.io/wiki for more details.

# Curl examples

to get you started:

## 1. Pairing

First, pair with Allow2 (this is using the username/password method):

```sh
curl -H "Content-Type: application/json" -X POST -d '{"user": "ALLOW2_ACCOUNT_EMAIL", "pass":"ALLOW2_ACCOUNT_PASS", "deviceToken": "jJ5GOIaJ028Ywt6K", "name":"Test Device 1" }' https://app.allow2.com:8443/api/pairDevice
```

this returns a payload with information that you pass back to the app to persist for future use against the service:
```json
{
  "status":"success",
  "pairId":12345,
  "token":"6742b233-de46-4c86-2ac9-7b9e5729f999",
  "name":"Test Device 1",
  "userId": 1234,
  "children":[
    { "id":682, "name":"Bob" },
    { "id":691, "name":"Grace" },
    { "id":1795,"name":"Fred"}
  ]
}
```

## 2. Checking and Logging Usage

Coming shortly
