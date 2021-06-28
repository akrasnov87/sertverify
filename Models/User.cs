using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SertCheck.Models
{
    [Table("pd_users", Schema = "core")]
    public class User
    {
        [Key]
        public int id { get; set; }

        /// <summary>
        /// логин
        /// </summary>
        public string c_login { get; set; }

        /// <summary>
        /// ФИО
        /// </summary>
        public string c_first_name { get; set; }

        /// <summary>
        /// Адрес эл. почты для рассылки
        /// </summary>
        public string c_email { get; set; }

        /// <summary>
        /// Признак отключенности
        /// </summary>
        public bool b_disabled { get; set; }

        /// <summary>
        /// Признак удаления
        /// </summary>
        public bool sn_delete { get; set; }

        public string c_description { get; set; }
    }
}
