# Ativar a assinatura SignPath

A candidatura do G321 Studio foi recusada. Este guia registra a integração preparada para a SignPath. O processo disponível para a versão 1.0.4 está na [seção de assinatura local](../README.md#assinatura-para-distribuição-no-github).

Após a aprovação, vincule o repositório `gleyssonsc/g321-studio` ao projeto SignPath usando o sistema confiável **GitHub.com**. Configure uma política de release que verifique a origem das compilações na branch `main` e exija aprovação manual na SignPath. Use um token com permissão **Submitter** para essa política. [Integração com GitHub](https://docs.signpath.io/trusted-build-systems/github) · [Verificação de origem](https://docs.signpath.io/origin-verification/)

Registre [SignPath-ArtifactConfiguration.xml](../installer/SignPath-ArtifactConfiguration.xml) no projeto SignPath. O XML seleciona apenas o EXE do instalador no ZIP do GitHub Actions. O nome do produto inclui os espaços armazenados no metadado do Inno Setup 6.7.3. O valor de versão enviado no campo `version` carrega os espaços do metadado e vem do build conferido contra o arquivo de projeto. Confira os dois campos no primeiro pedido real. [Configuração de artefatos](https://docs.signpath.io/artifact-configuration/examples#msi-and-pe-file-metadata-restriction)

Cadastre os valores em **Settings → Environments → signpath** no GitHub. O ambiente aceita a branch `main`.

| Nome | Tipo | Valor na SignPath |
| --- | --- | --- |
| `SIGNPATH_API_TOKEN` | Secret | Token do solicitante |
| `SIGNPATH_ORGANIZATION_ID` | Variable | ID da organização |
| `SIGNPATH_PROJECT_SLUG` | Variable | Slug do projeto |
| `SIGNPATH_SIGNING_POLICY_SLUG` | Variable | Slug da política de release |
| `SIGNPATH_ARTIFACT_CONFIGURATION_SLUG` | Variable | Slug da configuração XML |

No [workflow do instalador](https://github.com/gleyssonsc/g321-studio/actions/workflows/build-offline-installer.yml), clique em **Run workflow**, escolha `main` e marque **Request SignPath signing of the offline installer from main**. A execução solicita a assinatura e aguarda até uma hora pela aprovação na SignPath. Se a espera expirar após a criação do pedido, confira esse pedido no painel da SignPath antes de iniciar outro.

Quando a assinatura voltar, o job verifica o certificado da **SignPath Foundation**, o carimbo de tempo e a versão. Baixe o EXE assinado e seu `SHA256SUMS.txt` em **Artifacts → g321-studio-offline-installer-signed**. Esse é um artefato do workflow. A publicação em GitHub Releases é feita pelo mantenedor após a validação do instalador.
