import os
import re

file_path = os.path.join("src", "app", "plugins", "plugins.routing.ts")

with open(file_path, "r") as file:
    content = file.read()

replacements = [
    (r"INSERT ROUTES HERE", "  },\nINSERT ROUTES HERE"),
    (r"INSERT ROUTES HERE", "      .then(m => m.BackendConfigurationPnModule)\nINSERT ROUTES HERE"),
    (r"INSERT ROUTES HERE", "    loadChildren: () => import('./modules/backend-configuration-pn/backend-configuration-pn.module')\nINSERT ROUTES HERE"),
    (r"INSERT ROUTES HERE", "    path: 'backend-configuration-pn',\nINSERT ROUTES HERE"),
    (r"INSERT ROUTES HERE", "  {\nINSERT ROUTES HERE"),
]

for pattern, replacement in replacements:
    content = re.sub(pattern, replacement, content, count=1)

with open(file_path, "w") as file:
    file.write(content)
