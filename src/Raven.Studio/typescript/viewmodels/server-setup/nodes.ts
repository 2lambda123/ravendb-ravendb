import setupStep = require("viewmodels/server-setup/setupStep");
import router = require("plugins/router");
import nodeInfo = require("models/setup/nodeInfo");

class nodes extends setupStep {

    constructor() {
        super();
        
        this.bindToCurrentInstance("removeNode");
    }
    
    save() {
        //TODO:
     
    }
    
    addNode() {
        this.model.nodes.push(new nodeInfo());
    }

    removeNode(node: nodeInfo) {
        this.model.nodes.remove(node);
    }

}

export = nodes;
