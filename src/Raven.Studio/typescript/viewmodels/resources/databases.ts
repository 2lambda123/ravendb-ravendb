import viewModelBase = require("viewmodels/viewModelBase");
import { DatabasesPage } from "../../components/pages/resources/databases/DatabasesPage";

class databases extends viewModelBase {
    view = { default: `<div class="databases flex-vertical absolute-fill content-margin" data-bind="react: reactOptions"></div>` };

    reactOptions = ko.pureComputed(() => {
        const props: Parameters<typeof DatabasesPage>[0] = {
            activeDatabase: this.activeDatabase()?.name
        }
        
        return ({
            component: DatabasesPage,
            props
        });
    });

    isUsingBootstrap5() {
        return true;
    }
}

export = databases;
