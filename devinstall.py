import os
import shutil

os.chdir(os.path.expanduser("~"))
print(os.getcwd())

# Define base paths
src_base = os.path.join("Documents", "workspace", "microting", "eform-backendconfiguration-plugin")
dst_base = os.path.join("Documents", "workspace", "microting", "eform-angular-frontend")

# Paths to remove and copy
paths = [
    (os.path.join("eform-client", "src", "app", "plugins", "modules", "backend-configuration-pn"),
     os.path.join("eform-client", "src", "app", "plugins", "modules", "backend-configuration-pn")),
    (os.path.join("eFormAPI", "Plugins", "BackendConfiguration.Pn"),
     os.path.join("eFormAPI", "Plugins", "BackendConfiguration.Pn")),
]

for dst_rel_path, src_rel_path in paths:
    dst_path = os.path.join(dst_base, dst_rel_path)
    src_path = os.path.join(src_base, src_rel_path)

    if os.path.exists(dst_path):
        shutil.rmtree(dst_path)

    shutil.copytree(src_path, dst_path)

# Ensure the Plugins directory exists
plugins_dir = os.path.join(dst_base, "eFormAPI", "Plugins")
os.makedirs(plugins_dir, exist_ok=True)

# Test files to remove
test_files_to_remove = [
    os.path.join("eform-client", "e2e", "Tests", "backend-configuration-settings"),
    os.path.join("eform-client", "e2e", "Tests", "backend-configuration-general"),
    os.path.join("eform-client", "e2e", "Page objects", "BackendConfiguration"),
    os.path.join("eform-client", "e2e", "Assets"),
    os.path.join("eform-client", "wdio-plugin-step2.conf.ts"),
    os.path.join("eform-client", "cypress", "e2e", "plugins", "backend-configuration-pn"),
]

for rel_path in test_files_to_remove:
    full_path = os.path.join(dst_base, rel_path)
    if os.path.exists(full_path):
        if os.path.isdir(full_path):
            shutil.rmtree(full_path)
        else:
            os.remove(full_path)

# Ensure the plugins directory exists within the Cypress structure
cypress_plugins_dir = os.path.join(dst_base, "eform-client", "cypress", "e2e", "plugins")
os.makedirs(cypress_plugins_dir, exist_ok=True)

# Test files to copy
test_files_to_copy = [
    (os.path.join("eform-client", "e2e", "Tests", "backend-configuration-settings"),
     os.path.join("eform-client", "e2e", "Tests", "backend-configuration-settings")),
    (os.path.join("eform-client", "e2e", "Tests", "backend-configuration-general"),
     os.path.join("eform-client", "e2e", "Tests", "backend-configuration-general")),
    (os.path.join("eform-client", "e2e", "Page objects", "BackendConfiguration"),
     os.path.join("eform-client", "e2e", "Page objects", "BackendConfiguration")),
    (os.path.join("eform-client", "e2e", "Assets"),
     os.path.join("eform-client", "e2e", "Assets")),
    (os.path.join("eform-client", "wdio-headless-plugin-step2a.conf.ts"),
     os.path.join("eform-client", "wdio-headless-plugin-step2a.conf.ts")),
    (os.path.join("eform-client", "wdio-headless-plugin-step2b.conf.ts"),
     os.path.join("eform-client", "wdio-headless-plugin-step2b.conf.ts")),
    (os.path.join("eform-client", "wdio-headless-plugin-step2c.conf.ts"),
     os.path.join("eform-client", "wdio-headless-plugin-step2c.conf.ts")),
    (os.path.join("eform-client", "wdio-headless-plugin-step2d.conf.ts"),
     os.path.join("eform-client", "wdio-headless-plugin-step2d.conf.ts")),
    (os.path.join("eform-client", "wdio-headless-plugin-step2e.conf.ts"),
     os.path.join("eform-client", "wdio-headless-plugin-step2e.conf.ts")),
    (os.path.join("eform-client", "wdio-headless-plugin-step2f.conf.ts"),
     os.path.join("eform-client", "wdio-headless-plugin-step2f.conf.ts")),
    (os.path.join("eform-client", "wdio-headless-plugin-step2g.conf.ts"),
     os.path.join("eform-client", "wdio-headless-plugin-step2g.conf.ts")),
    (os.path.join("eform-client", "wdio-headless-plugin-step2h.conf.ts"),
     os.path.join("eform-client", "wdio-headless-plugin-step2h.conf.ts")),
    (os.path.join("eform-client", "wdio-headless-plugin-step2i.conf.ts"),
     os.path.join("eform-client", "wdio-headless-plugin-step2i.conf.ts")),
    (os.path.join("eform-client", "wdio-headless-plugin-step2j.conf.ts"),
     os.path.join("eform-client", "wdio-headless-plugin-step2j.conf.ts")),
    (os.path.join("eform-client", "cypress", "e2e", "plugins", "backend-configuration-pn"),
     os.path.join("eform-client", "cypress", "e2e", "plugins", "backend-configuration-pn")),
    (os.path.join("eform-client", "cypress", "fixtures"),
     os.path.join("eform-client", "cypress", "fixtures")),
]

for src_rel_path, dst_rel_path in test_files_to_copy:
    src_path = os.path.join(src_base, src_rel_path)
    dst_path = os.path.join(dst_base, dst_rel_path)

    if os.path.isdir(src_path):
        shutil.copytree(src_path, dst_path)
    else:
        shutil.copy2(src_path, dst_path)
