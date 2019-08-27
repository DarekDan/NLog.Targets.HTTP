# NLog.Targets.HTTP

NLog.Targets.HTTP is a HTTP POST target for NLog. 
Combined with JSON formatter it can be used to send events to an 
instance of Splunk and other HTTP based collectors.

## Getting started

```xml
<?xml version="1.0" encoding="utf-8" ?>
<nlog xmlns="http://www.nlog-project.org/schemas/NLog.xsd" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" >
  <targets>
    <target name='splunk' 
            type='HTTP' URL='server:port/services/collector'
            Authorization='Splunk auth-token' 
            BatchSize='100'>
      <layout type='JsonLayout'>
        <attribute name='sourcetype' layout='_json' />
        <attribute name='host' layout='myHostName' />
        <attribute name='event' encode='false'>
          <layout type='JsonLayout'>
            <attribute name='level' layout='${level:upperCase=true}' />
            <attribute name='source' layout='${logger}' />
            <attribute name='thread' layout='${threadid}' />
            <attribute name='message' layout='${message}' />
            <attribute name='utc' layout='${date:universalTime=true:format=yyyy-MM-dd HH\:mm\:ss.fff}' />
          </layout>
        </attribute>
      </layout>
    </target>
  </targets> <rules>
    <logger name="*" minlevel="Debug" writeTo="splunk" />
  </rules>
</nlog>
```

