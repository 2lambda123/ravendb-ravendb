/// <reference path="../../../typings/tsd.d.ts"/>

type configurationMode = "Unsecured" | "Secured" | "LetsEncrypt"; //tODO use enum SetupMode

import unsecureSetup = require("models/setup/unsecureSetup");
import licenseInfo = require("models/setup/licenseInfo");
import domainInfo = require("models/setup/domainInfo");
import nodeInfo = require("models/setup/nodeInfo");


class serverSetup {
   static default = new serverSetup();
   
   mode = ko.observable<configurationMode>();
   
   license = ko.observable<licenseInfo>(new licenseInfo());
   
   domain = ko.observable<domainInfo>(new domainInfo());
   
   unsecureSetup = ko.observable<unsecureSetup>(new unsecureSetup());
   
   nodes = ko.observableArray<nodeInfo>([new nodeInfo()]);
}

export = serverSetup;
