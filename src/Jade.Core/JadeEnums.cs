using System;

namespace Jade.Core
{
    public enum JadeObjectClass : uint
    {
        Container = 196613,
        OrganizationalUnit = 196614,
        User = 196621,
        Group = 196622
    }

    [Flags]
    public enum UserAccountControl : uint
    {
        AccountDisable = 0x0002,
        NormalAccount = 0x0200,
        DontExpirePassword = 0x10000
    }

    /// <summary>
    /// Niveis corporativos de acesso (RBAC / Grupos de Seguranca do Active Directory)
    /// impostos pelo Controlador de Dominio Jade.
    /// </summary>
    public enum JadeAccessLevel : uint
    {
        /// <summary>
        /// RID 513 (Domain Users): Autenticacao, leitura basica e troca apenas da propria senha.
        /// </summary>
        DomainUser = 0,

        /// <summary>
        /// RID 546 (Security Auditors): Somente leitura de todo o diretorio, bloqueado para escrita.
        /// </summary>
        SecurityAuditor = 1,

        /// <summary>
        /// RID 548 (Account Operators / Helpdesk IAM): Criacao, desativacao, redefinicao de senha e exclusao
        /// de contas comuns. Bloqueado por AdminSDHolder de alterar contas DomainAdmin.
        /// </summary>
        AccountOperator = 2,

        /// <summary>
        /// RID 512 (Domain Admins / adminCount=1): Controle total irrestrito sobre o dominio e FSMO RID Master.
        /// </summary>
        DomainAdmin = 3
    }
}
