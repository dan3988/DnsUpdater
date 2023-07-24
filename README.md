# DnsUpdater
A Windows service that detects IP address changes and sends user defined HTTP requests when a change is detected.

## Configuration
### BaseDir
This defaults to a folder named DnsUpdater inside shared application data folder (`C:\ProgramData`). This is where the user specified appsettings.json lives and a file used by the service to track the last IP address is written.
### Logging
By default, the app will log messages to `%BaseDir%\logs` and log warning and error messages to the windows event log.

### Config
An object containing settings for the service

##### IpProvider
A url that is used to determine the current IP of the machine running the service. The response needs to be plain text and a valid IPv4 or IPv6 string.
##### ChangeDelay (optional)
The number of milliseconds to wait after a network configuration change is detected before invoking the actions, to prevent them from being invoked multiple times unnecessarily.
##### Ignore (optional)
A list of IP addresses or host names to ignore if your machine is connected to a VPN.
##### HistoryFile (optional)
A path to a file that the service will log the date and IP to when a change is detected.
##### Actions
A list of HTTP request templates to send when a change is detected. The templates can access properties stored in the configuration file, or environment variables by putting `${Name}` in the string. There are also the following pre-defined values:

* `IP` - The current IP
* `OLDIP` - The IP address before the change
* `DATE` - The current DateTime

Here is an example of a configuration to update a domain using google domains:
```json
{
  "Config": {
    "IpProvider": "http://checkip.amazonaws.com/nic/update",
    "Actions": [
      {
        "Location": "https://domains.google.com/nic/update?hostname=${Google:HostName}&myip=${IP}",
        "Headers": {
          "Authorization": "Basic ${Google:Credentials}"
        }
      }
    ]
  },
  "Google": {
    "HostName": "your.domain.name",
    "Credentials": "base64 credentials"
  }
}
```
